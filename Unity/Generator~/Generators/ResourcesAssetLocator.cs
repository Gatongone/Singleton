using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace Singleton.Generator
{
    /// <summary>
    /// Reads the path which an asset of a <c>Resources</c> singleton is loaded by.<para/>
    /// The path is read out of an index which the editor half of the package writes into the <c>obj</c> folder of the
    /// project, and which the <c>AssetDatabase</c> answered: a line of it names a script, the kind of asset, and the
    /// path that asset is loaded by, so the asset of a type is the first line which names the script which declares the
    /// type. The project is found through the scripts being compiled, and its folders are walked only where the index
    /// has no line for that script.
    /// </summary>
    /// <remarks>
    /// A path in a <c>Resources</c> folder is not something a compilation knows: the asset is a file of a project and
    /// the compiler is handed the scripts of it alone, which is why the index is written at all.<para/>
    /// Walking asks an asset the same question the index answers - whether it names the script which the type was
    /// declared in - and reads the answer out of the asset's own text rather than from Unity: an <c>.asset</c> of a
    /// <c>ScriptableObject</c> and a <c>.prefab</c> of a <c>MonoBehaviour</c> both name their script the same way, so
    /// only the extension tells them apart.
    /// </remarks>
    internal sealed class ResourcesAssetLocator
    {
        /// <summary>The identifier which Unity writes into the meta of an asset.</summary>
        private static readonly Regex s_MetaGuid = new(
            @"^guid:\s*(?<guid>[0-9a-fA-F]{32})\s*$", RegexOptions.Multiline | RegexOptions.Compiled);

        /// <summary>The script which an asset names, as the project writes it.</summary>
        private static readonly Regex s_ScriptGuid = new(
            @"m_Script:\s*\{[^}]*?guid:\s*(?<guid>[0-9a-fA-F]{32})", RegexOptions.Compiled);

        /// <summary>The folders of a project which a <c>Resources</c> path is written against.</summary>
        private const string RESOURCES_FOLDER = "Resources";

        /// <summary>The file, under the project, which the editor half of the package writes the index into.</summary>
        private const string INDEX_FILE = "obj/Singleton.txt";

        /// <summary>What an asset which a <c>MonoBehaviour</c> is loaded from is written as.</summary>
        private const string PREFAB_KIND = "prefab";

        /// <summary>What an asset which a <c>ScriptableObject</c> is loaded from is written as.</summary>
        private const string ASSET_KIND = "asset";

        private readonly string? m_ProjectRoot;
        private Dictionary<string, string>? m_Index;
        private List<string>? m_Prefabs;
        private List<string>? m_ScriptableObjects;

        /// <summary>
        /// Read the assets of a Unity project.
        /// </summary>
        /// <param name="projectRoot">The folder which holds <c>Assets</c>, or <c>null</c> where it is not known.</param>
        public ResourcesAssetLocator(string? projectRoot) => m_ProjectRoot = projectRoot;

        /// <summary>The folder which holds <c>Assets</c>, or <c>null</c> where it is not known.</summary>
        public string? ProjectRoot => m_ProjectRoot;

        /// <summary>
        /// Find the Unity project which the scripts of a compilation belong to.
        /// </summary>
        /// <remarks>
        /// The project is read off what the compiler was handed rather than off a path of the machine alone. Unity
        /// hands the project being compiled to the compiler as an additional file, and that is the one place it is
        /// written down: the scripts of a package lie beside a project rather than inside one, so a compilation of the
        /// tests of a package holds no path which names the project at all. A compilation which was handed no such
        /// file is read through the paths of its trees, and one which names no project in either is read against the
        /// folder the compiler runs in, which is the project a Unity editor compiles in.
        /// </remarks>
        /// <param name="compilation">The compilation.</param>
        /// <param name="additional">The files which the compilation was handed beside its trees.</param>
        /// <returns>The folder which holds <c>Assets</c>, or <c>null</c> where there is none.</returns>
        public static string? FindProjectRoot(Compilation compilation, IEnumerable<AdditionalText> additional)
        {
            foreach (var text in additional)
            {
                var candidate = text.GetText()?.ToString().Trim();
                if (IsProject(candidate)) return candidate;
            }

            foreach (var tree in compilation.SyntaxTrees)
            {
                var file = tree.FilePath;
                if (string.IsNullOrEmpty(file)) continue;

                var directory = Path.GetDirectoryName(file);
                while (!string.IsNullOrEmpty(directory))
                {
                    if (string.Equals(Path.GetFileName(directory), "Assets", StringComparison.Ordinal))
                    {
                        var parent = Path.GetDirectoryName(directory);
                        if (IsProject(parent)) return parent;
                    }

                    var next = Path.GetDirectoryName(directory);
                    if (string.IsNullOrEmpty(next) || string.Equals(next, directory, StringComparison.Ordinal)) break;
                    directory = next;
                }
            }

            return IsProject(Environment.CurrentDirectory) ? Environment.CurrentDirectory : null;
        }

        /// <summary>
        /// Whether a path is a Unity project, which is a folder which holds what every project has.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns>Whether it is one.</returns>
        private static bool IsProject(string? path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            return Directory.Exists(Path.Combine(path, "ProjectSettings")) ||
                   Directory.Exists(Path.Combine(path, "Packages")) ||
                   Directory.Exists(Path.Combine(path, "Assets"));
        }

        /// <summary>
        /// Read the path which an asset of a type is loaded by.
        /// </summary>
        /// <remarks>
        /// What answers is the index which the editor half of the package writes, and the folders of the project are
        /// walked only where the index has nothing to say: a compilation happens far more often than a project changes,
        /// so a path which was read by walking makes every compilation of every project pay for a scan which the
        /// AssetDatabase already did once.
        /// </remarks>
        /// <param name="type">The singleton which the asset holds.</param>
        /// <param name="prefab">
        /// Whether the asset is a prefab, which is what a <c>MonoBehaviour</c> is loaded from, rather than an asset file,
        /// which is what a <c>ScriptableObject</c> is loaded from.
        /// </param>
        /// <returns>The path, or <c>null</c> where no asset of the type was found.</returns>
        public string? Find(INamedTypeSymbol type, bool prefab)
        {
            if (m_ProjectRoot == null) return null;

            var script = type.DeclaringSyntaxReferences.Length > 0
                ? type.DeclaringSyntaxReferences[0].SyntaxTree.FilePath
                : null;

            if (string.IsNullOrEmpty(script)) return null;

            var guid = ReadScriptGuid(script!);
            if (guid == null) return null;

            var kind = prefab ? PREFAB_KIND : ASSET_KIND;
            if (Index().TryGetValue(guid + " " + kind, out var indexed)) return indexed;

            // The index has no asset of the type, which is either an asset which is not in it yet - the first
            // compilation after the package was added, or one which was moved while another compilation was failing -
            // or an asset which is not there at all. Which of the two it is is what the folders are walked to find
            // out, and it is the only thing they are walked for.
            return Walk(guid, prefab);
        }

        /// <summary>
        /// Read the index which the editor half of the package writes into the <c>obj</c> folder of the project.
        /// </summary>
        /// <remarks>
        /// A line is <c>&lt;script guid&gt; &lt;prefab|asset&gt; &lt;path&gt;</c>, the lines are written in the order of
        /// the paths, so that the first one of them which names a script is the one which is answered with, and every
        /// line which is not one of those is skipped rather than refused, because a file which was half written by an
        /// editor which was closed while it wrote is worth more read for what it holds than given up over what it does
        /// not.
        /// </remarks>
        /// <returns>The path of each script and kind, or <c>null</c> where there is no index file.</returns>
        private Dictionary<string, string> Index()
        {
            if (m_Index != null) return m_Index;

            m_Index = new Dictionary<string, string>(StringComparer.Ordinal);
            var file = Path.Combine(m_ProjectRoot!, INDEX_FILE);

            try
            {
                if (!File.Exists(file)) return m_Index;

                foreach (var line in File.ReadAllLines(file))
                {
                    if (line.Length == 0 || line[0] == '#') continue;

                    var kind = line.IndexOf(' ');
                    if (kind < 0) continue;

                    var path = line.IndexOf(' ', kind + 1);
                    if (path < 0) continue;

                    var key = line.Substring(0, path);
                    if (!m_Index.ContainsKey(key)) m_Index[key] = line.Substring(path + 1);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // An index which cannot be read is an index which has nothing to say, and what is left is the walk.
            }

            return m_Index;
        }

        /// <summary>
        /// Find the path of an asset by walking the folders of the project.
        /// </summary>
        /// <param name="guid">The identifier of the script which the asset names.</param>
        /// <param name="prefab">Whether the assets are prefabs rather than asset files.</param>
        /// <returns>The path, or <c>null</c> where no asset of the type was found.</returns>
        private string? Walk(string guid, bool prefab)
        {
            var found = new List<string>();

            foreach (var candidate in Candidates(prefab))
            {
                string text;
                try
                {
                    text = File.ReadAllText(candidate);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                if (!Names(text, guid)) continue;

                if (ToResourcesPath(candidate) is { } path) found.Add(path);
            }

            // What the index would have answered is the first asset of the project in the order of the paths, so what
            // the walk answers is the same one: a project answers with the same asset whether or not the editor has
            // written the index since it last changed.
            found.Sort(StringComparer.Ordinal);
            return found.Count > 0 ? found[0] : null;
        }

        /// <summary>
        /// Read the identifier which Unity gave the script of a type.
        /// </summary>
        /// <param name="script">The path of the script file.</param>
        /// <returns>The identifier, or <c>null</c> where the file has no meta yet.</returns>
        private static string? ReadScriptGuid(string script)
        {
            var meta = script + ".meta";
            try
            {
                if (!File.Exists(meta)) return null;

                var match = s_MetaGuid.Match(File.ReadAllText(meta));
                return match.Success ? match.Groups["guid"].Value : null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        /// Whether an asset names a script.
        /// </summary>
        /// <param name="asset">The text of the asset.</param>
        /// <param name="guid">The identifier of the script.</param>
        /// <returns>Whether the asset names it.</returns>
        private static bool Names(string asset, string guid)
        {
            foreach (Match match in s_ScriptGuid.Matches(asset))
            {
                if (string.Equals(match.Groups["guid"].Value, guid, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        /// <summary>
        /// Read every asset of a kind which lies under a <c>Resources</c> folder of the project, in the order which the
        /// paths are read in, so that the asset which is answered with is the same for every compilation of it.
        /// </summary>
        /// <param name="prefab">Whether the assets are prefabs rather than asset files.</param>
        /// <returns>The paths of the assets.</returns>
        private List<string> Candidates(bool prefab)
        {
            var cached = prefab ? m_Prefabs : m_ScriptableObjects;
            if (cached != null) return cached;

            var found = new List<string>();
            var assets = Path.Combine(m_ProjectRoot!, "Assets");
            var extension = prefab ? "*.prefab" : "*.asset";

            if (Directory.Exists(assets))
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var folder in ResourcesFolders(assets))
                {
                    IEnumerable<string> files;
                    try
                    {
                        files = Directory.EnumerateFiles(folder, extension, SearchOption.TopDirectoryOnly);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        continue;
                    }

                    foreach (var file in files)
                    {
                        if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                        if (seen.Add(file)) found.Add(file);
                    }
                }
            }

            found.Sort(StringComparer.Ordinal);
            if (prefab) m_Prefabs = found;
            else m_ScriptableObjects = found;
            return found;
        }

        /// <summary>
        /// Read every folder of a project which is named <c>Resources</c>.
        /// </summary>
        /// <param name="assets">The <c>Assets</c> folder of the project.</param>
        /// <returns>The paths of the folders.</returns>
        private static string[] ResourcesFolders(string assets)
        {
            string[] found;
            try
            {
                found = Directory.GetDirectories(assets, RESOURCES_FOLDER, SearchOption.AllDirectories);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }

            Array.Sort(found, StringComparer.Ordinal);
            return found;
        }

        /// <summary>
        /// Read the path which <c>Resources.Load</c> loads an asset by.
        /// </summary>
        /// <remarks>
        /// The path of a <c>Resources</c> asset is what is written under the first <c>Resources</c> folder which holds
        /// it, without the extension of the file: a folder which is named <c>Resources</c> inside another one is read
        /// against the outer one as well, and the outer one is what Unity names the asset by.
        /// </remarks>
        /// <param name="file">The path of the asset file.</param>
        /// <returns>The path, or <c>null</c> where the file is not under a <c>Resources</c> folder.</returns>
        private static string? ToResourcesPath(string file)
        {
            var normalized = "/" + file.Replace('\\', '/').TrimStart('/');
            var marker = "/" + RESOURCES_FOLDER + "/";
            var index = normalized.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0) return null;

            var path = normalized.Substring(index + marker.Length);
            var extension = Path.GetExtension(path);
            return extension.Length > 0 ? path.Substring(0, path.Length - extension.Length) : path;
        }
    }
}