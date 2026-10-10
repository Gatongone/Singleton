#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace Singleton.Editor
{
    /// <summary>
    /// Writes the index of the assets which a singleton may be loaded from into the <c>obj</c> folder of the project.
    /// </summary>
    /// <remarks>
    /// The index is what the source generator reads instead of walking the project: a compilation happens far more
    /// often than a project changes, and reading a path by walking is a scan which every compilation pays for, and
    /// which the AssetDatabase could have done once. What is written is a line for every script of every
    /// <c>.prefab</c> and <c>.asset</c> under a <c>Resources</c> folder, because what a generator needs is not "which
    /// asset holds this type" - that is a question about a compilation which is running - but "which asset holds the
    /// script which declares this type", which is a question about the project and which the AssetDatabase answers.
    /// </remarks>
    [InitializeOnLoad]
    internal static class SingletonAssetIndex
    {
        /// <summary>Where the index is written, under the project.</summary>
        private const string WRITTEN = "obj/Singleton.txt";

        /// <summary>The folder which an asset is loaded by name from.</summary>
        private const string RESOURCES = "Resources";

        /// <summary>What an asset which a <c>MonoBehaviour</c> is loaded from is written as.</summary>
        private const string PREFAB = "prefab";

        /// <summary>What an asset which a <c>ScriptableObject</c> is loaded from is written as.</summary>
        private const string ASSET = "asset";

        /// <summary>Whether a call to <see cref="Write"/> is already waiting on the editor's idle callback, which is
        /// what makes a burst of imports write the index once rather than once for each asset.</summary>
        private static bool s_Queued;

        static SingletonAssetIndex() => EditorApplication.delayCall += Write;

        /// <summary>
        /// Asks for the index to be written once the editor is idle.<para/>
        /// An import of one asset is an import of many, so what a burst of imports asks for is one write rather than
        /// one for each file.
        /// </summary>
        public static void Queue()
        {
            if (s_Queued) return;

            s_Queued = true;
            EditorApplication.delayCall += () =>
            {
                s_Queued = false;
                Write();
            };
        }

        /// <summary>
        /// Write the index where it differs from the one which is there, and ask for a compilation where it does.
        /// </summary>
        /// <remarks>
        /// Asking for a compilation is what makes the file worth writing: it lies in the <c>obj</c> folder, which is
        /// not a folder Unity watches, so nothing else would tell a compilation which is already running that the
        /// paths it read are the ones from before. What is asked for is asked for only where the index changed, which
        /// is what stops the write and the compilation from asking each other for another one forever.
        /// </remarks>
        public static void Write()
        {
            try
            {
                var wanted = Index();
                if (!Save(wanted)) return;

                CompilationPipeline.RequestScriptCompilation();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("The Singleton asset index could not be written, so the paths of Resources singletons " +
                                 $"will be read by walking the project. {exception}");
            }
        }

        /// <summary>
        /// Read the index of every asset a singleton may be loaded from.
        /// </summary>
        /// <returns>The file.</returns>
        private static string Index()
        {
            var found = new List<(string Kind, string Path, string Script)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var asset in AssetDatabase.GetAllAssetPaths())
            {
                if (Kind(asset) is not { } kind) continue;
                if (Path(asset) is not { } path) continue;

                foreach (var script in Scripts(asset))
                {
                    if (!seen.Add(script + " " + kind + " " + path)) continue;
                    found.Add((kind, path, script));
                }
            }

            // What a generator answers with is the first line which names the script of a type, so the lines are
            // written in the order which that answer is meant to be read in, which is the order of the paths.
            found.Sort(Compare);

            var text = new StringBuilder();
            text.Append("# Unity.Singleton.CodeGen wrote this file, and the Singleton source generator reads it.\n");
            text.Append("# <script guid> <prefab|asset> <path the asset is loaded by>\n");
            foreach (var entry in found) text.Append(entry.Script).Append(' ').Append(entry.Kind).Append(' ').Append(entry.Path).Append('\n');
            return text.ToString();
        }

        /// <summary>
        /// Write the index where it is not the one which is there.
        /// </summary>
        /// <param name="wanted">The index which was read.</param>
        /// <returns>Whether what is there is now another index than the one which was.</returns>
        private static bool Save(string wanted)
        {
            var file = System.IO.Path.Combine(Root(), WRITTEN);
            if (File.Exists(file) && File.ReadAllText(file) == wanted) return false;

            var directory = System.IO.Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            File.WriteAllText(file, wanted);
            return true;
        }

        /// <summary>
        /// Read the scripts which an asset holds.
        /// </summary>
        /// <remarks>
        /// What an asset is built on is what the AssetDatabase says it depends on, which is answered without loading
        /// it: an asset in a project is one of thousands, and loading every one of them to ask which script it carries
        /// is what makes an index worth having.
        /// </remarks>
        /// <param name="asset">The asset.</param>
        /// <returns>The identifiers of the scripts.</returns>
        private static List<string> Scripts(string asset)
        {
            var scripts = new List<string>();

            string[] dependencies;
            try
            {
                dependencies = AssetDatabase.GetDependencies(asset, recursive: false);
            }
            catch (Exception)
            {
                // An asset whose dependencies cannot be read is one which carries no script as far as this index is
                // concerned, and one asset which is left out of it is one path which is read by walking.
                return scripts;
            }

            foreach (var dependency in dependencies)
            {
                if (!dependency.EndsWith(".cs", StringComparison.Ordinal)) continue;

                var guid = AssetDatabase.AssetPathToGUID(dependency);
                if (!string.IsNullOrEmpty(guid)) scripts.Add(guid);
            }

            return scripts;
        }

        /// <summary>
        /// Read what an asset is, where it is one which a singleton is loaded from.
        /// </summary>
        /// <param name="asset">The asset.</param>
        /// <returns>What it is, or <c>null</c> where a singleton is not loaded from it.</returns>
        private static string? Kind(string asset) => System.IO.Path.GetExtension(asset).ToLowerInvariant() switch
        {
            ".prefab" => PREFAB,
            ".asset" => ASSET,
            _ => null
        };

        /// <summary>
        /// Read the path which an asset is loaded by, where it is one which a <c>Resources</c> folder holds.
        /// </summary>
        /// <remarks>
        /// The path is what is written under the first <c>Resources</c> folder which holds the asset, without the
        /// extension: a folder named <c>Resources</c> inside another one is read against the outer one as well, and the
        /// outer one is what Unity names the asset by.
        /// </remarks>
        /// <param name="asset">The asset.</param>
        /// <returns>The path, or <c>null</c> where the asset is not under a <c>Resources</c> folder.</returns>
        private static string? Path(string asset)
        {
            var normalized = asset.Replace('\\', '/');
            var marker = "/" + RESOURCES + "/";
            var index = normalized.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0) return null;

            var path = normalized.Substring(index + marker.Length);
            var extension = System.IO.Path.GetExtension(path);
            return extension.Length > 0 ? path.Substring(0, path.Length - extension.Length) : path;
        }

        /// <summary>
        /// Read the folder which holds <c>Assets</c>, which is the project the index is written into.
        /// </summary>
        /// <returns>The folder.</returns>
        private static string Root() => Directory.GetParent(Application.dataPath)!.FullName;

        /// <summary>
        /// Read which of two lines of the index is written first.
        /// </summary>
        /// <param name="left">A line.</param>
        /// <param name="right">A line.</param>
        /// <returns>Which of the two comes first.</returns>
        private static int Compare((string Kind, string Path, string Script) left, (string Kind, string Path, string Script) right)
        {
            var order = string.CompareOrdinal(left.Kind, right.Kind);
            if (order != 0) return order;

            order = string.CompareOrdinal(left.Path, right.Path);
            return order != 0 ? order : string.CompareOrdinal(left.Script, right.Script);
        }
    }

    /// <summary>
    /// Writes the index again where a <c>Resources</c> folder was imported into, or moved, or taken away.
    /// </summary>
    /// <remarks>
    /// What is imported is read for whether it touches a <c>Resources</c> folder at all, because an editor imports an
    /// asset for every change to a project and almost none of them change what a singleton is loaded from.
    /// </remarks>
    internal sealed class SingletonAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (!Touches(imported) && !Touches(deleted) && !Touches(moved) && !Touches(movedFrom)) return;

            SingletonAssetIndex.Queue();
        }

        /// <summary>
        /// Whether any of the paths which an import named lies under a <c>Resources</c> folder.
        /// </summary>
        /// <param name="paths">The paths.</param>
        /// <returns>Whether any of them does.</returns>
        private static bool Touches(string[] paths)
        {
            foreach (var path in paths)
            {
                if (path.Replace('\\', '/').Contains("/Resources/")) return true;
            }

            return false;
        }
    }
}