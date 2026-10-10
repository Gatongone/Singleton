using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace Singleton.Generator
{
    /// <summary>
    /// Reads the path which the asset of a singleton is loaded by.<para/>
    /// A scene and a resource of Godot are text files, and both name the script they were built from the same way:
    /// <c>[ext_resource type="Script" path="res://Player.cs" id="..."]</c>. The asset of a type is therefore the first
    /// file which names the script which declares that type, and the path it is loaded by is the Godot path of that
    /// file.
    /// </summary>
    /// <remarks>
    /// This is what the package of Unity does through an <c>AssetDatabase</c>, and a Godot project needs none: the
    /// files are read here directly, which is both the answer and the only answer, because Godot keeps no index which a
    /// compilation can be handed.<para/>
    /// What is walked is the whole project, which is why the path is read once per singleton and the list of candidates
    /// is kept: a compilation happens far more often than a project changes.
    /// </remarks>
    internal sealed class GodotAssetLocator
    {
        /// <summary>The file which makes a folder a Godot project.</summary>
        private const string PROJECT_FILE = "project.godot";

        /// <summary>The folder which holds what Godot imported, which is not where a scene of the project is.</summary>
        private const string IMPORT_FOLDER = ".godot";

        /// <summary>The path of the script which a scene or a resource names.</summary>
        private static readonly Regex s_ScriptPath = new(
            @"path\s*=\s*""(?<path>res://[^""]+?\.cs)""", RegexOptions.Compiled);

        private readonly string? m_ProjectRoot;
        private List<string>? m_Scenes;
        private List<string>? m_Resources;

        /// <summary>
        /// Read the assets of a Godot project.
        /// </summary>
        /// <param name="projectRoot">The folder which holds <c>project.godot</c>, or <c>null</c> where it is not known.</param>
        public GodotAssetLocator(string? projectRoot) => m_ProjectRoot = projectRoot;

        /// <summary>The folder which holds <c>project.godot</c>, or <c>null</c> where it is not known.</summary>
        public string? ProjectRoot => m_ProjectRoot;

        /// <summary>
        /// Find the Godot project which the scripts of a compilation belong to.
        /// </summary>
        /// <remarks>
        /// The project is the folder which holds <c>project.godot</c>, and it is read by walking up from the files the
        /// compiler was handed: a Godot project keeps its scripts inside itself, so the first folder above a script
        /// which holds that file is the project. A compilation which was handed a project named outright is answered
        /// with it, which is what a project which builds from another folder does.
        /// </remarks>
        /// <param name="compilation">The compilation.</param>
        /// <param name="additional">The files which the compilation was handed beside its trees.</param>
        /// <returns>The folder which holds <c>project.godot</c>, or <c>null</c> where there is none.</returns>
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

                for (var directory = Path.GetDirectoryName(file);
                     !string.IsNullOrEmpty(directory);
                     directory = Path.GetDirectoryName(directory))
                {
                    if (IsProject(directory)) return directory;

                    var next = Path.GetDirectoryName(directory);
                    if (string.IsNullOrEmpty(next) || string.Equals(next, directory, StringComparison.Ordinal)) break;
                }
            }

            return IsProject(Environment.CurrentDirectory) ? Environment.CurrentDirectory : null;
        }

        /// <summary>
        /// Whether a path is a Godot project, which is a folder which holds what every project has.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns>Whether it is one.</returns>
        private static bool IsProject(string? path) =>
            !string.IsNullOrEmpty(path) && File.Exists(Path.Combine(path, PROJECT_FILE));

        /// <summary>
        /// Whether the script which declares a type is a file of the project.
        /// </summary>
        /// <remarks>
        /// A type which is declared outside the project - by a linked file, or by a package which is built beside it -
        /// has no path a scene of the project could name, so there is nothing to walk for it and the two are worth
        /// telling apart: one is a project which holds no asset of the type, and the other is a type which no asset of
        /// the project ever could hold.
        /// </remarks>
        /// <param name="type">The type.</param>
        /// <returns>Whether it is.</returns>
        public bool IsInProject(INamedTypeSymbol type)
        {
            if (m_ProjectRoot == null) return false;

            var script = type.DeclaringSyntaxReferences.Length > 0
                ? type.DeclaringSyntaxReferences[0].SyntaxTree.FilePath
                : null;

            return !string.IsNullOrEmpty(script) && ToGodotPath(script!) != null;
        }

        /// <summary>
        /// Read the path which the asset of a type is loaded by.
        /// </summary>
        /// <param name="type">The singleton which the asset holds.</param>
        /// <param name="scene">
        /// Whether the asset is a scene, which is what a <c>Node</c> is loaded from, rather than a resource, which is
        /// what a <c>Resource</c> is loaded from.
        /// </param>
        /// <returns>The path, or <c>null</c> where no asset of the type was found.</returns>
        public string? Find(INamedTypeSymbol type, bool scene)
        {
            if (m_ProjectRoot == null) return null;

            var script = type.DeclaringSyntaxReferences.Length > 0
                ? type.DeclaringSyntaxReferences[0].SyntaxTree.FilePath
                : null;

            if (string.IsNullOrEmpty(script)) return null;

            var named = ToGodotPath(script!);
            if (named == null) return null;

            var found = new List<string>();
            foreach (var file in Candidates(scene))
            {
                string text;
                try
                {
                    text = File.ReadAllText(file);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                if (!Names(text, named)) continue;
                if (ToGodotPath(file) is { } path) found.Add(path);
            }

            // A project answers with the same asset for every compilation of it, so the first one in the order of the
            // paths is the one which is answered with rather than whichever the walk happened to reach first.
            found.Sort(StringComparer.Ordinal);
            return found.Count > 0 ? found[0] : null;
        }

        /// <summary>
        /// Whether an asset names a script.
        /// </summary>
        /// <param name="asset">The text of the asset.</param>
        /// <param name="script">The Godot path of the script.</param>
        /// <returns>Whether the asset names it.</returns>
        private static bool Names(string asset, string script)
        {
            foreach (Match match in s_ScriptPath.Matches(asset))
            {
                if (string.Equals(match.Groups["path"].Value, script, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        /// <summary>
        /// Read the Godot path of a file of the project, which is what a path is written as in a scene.
        /// </summary>
        /// <param name="file">The path of the file.</param>
        /// <returns>The Godot path, or <c>null</c> where the file is not one of the project.</returns>
        private string? ToGodotPath(string file) => ToGodotPath(m_ProjectRoot!, file);

        /// <summary>
        /// Read the Godot path of a file which lies under a project.
        /// </summary>
        /// <param name="root">The folder which holds <c>project.godot</c>.</param>
        /// <param name="file">The path of the file.</param>
        /// <returns>The Godot path, or <c>null</c> where the file is not under the project.</returns>
        private static string? ToGodotPath(string root, string file)
        {
            var full = Path.GetFullPath(file).Replace('\\', '/');
            var project = Path.GetFullPath(root).Replace('\\', '/').TrimEnd('/');

            if (!full.StartsWith(project + "/", StringComparison.Ordinal)) return null;
            return "res://" + full.Substring(project.Length + 1);
        }

        /// <summary>
        /// Read every scene or resource of the project, in the order the paths are read in.
        /// </summary>
        /// <param name="scene">Whether the assets are scenes rather than resources.</param>
        /// <returns>The paths of the assets.</returns>
        private List<string> Candidates(bool scene)
        {
            var cached = scene ? m_Scenes : m_Resources;
            if (cached != null) return cached;

            var found = new List<string>();
            var extension = scene ? "*.tscn" : "*.tres";

            try
            {
                foreach (var file in Directory.EnumerateFiles(m_ProjectRoot!, extension, SearchOption.AllDirectories))
                {
                    if (Under(file, IMPORT_FOLDER)) continue;
                    found.Add(file);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A project which cannot be walked is one whose assets are the ones which were read before the walk
                // stopped, and a singleton which needs a path that was not read is reported as one which has none.
            }

            found.Sort(StringComparer.Ordinal);
            if (scene) m_Scenes = found;
            else m_Resources = found;
            return found;
        }

        /// <summary>
        /// Whether a file lies inside a folder of the project with a name, at any depth.
        /// </summary>
        /// <param name="file">The path of the file.</param>
        /// <param name="folder">The name of the folder.</param>
        /// <returns>Whether it does.</returns>
        private static bool Under(string file, string folder)
        {
            var parts = file.Replace('\\', '/').Split('/');
            return Array.IndexOf(parts, folder) >= 0;
        }
    }
}
