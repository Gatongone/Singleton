@tool
extends EditorPlugin

## Wires the package into a project which installed it as an addon.
##
## Two halves of the package have to be known to the build rather than to the engine, and neither can be handed over by a
## folder alone: the generator has to be named to the compiler, and the weaver has to be run after the assembly was
## written. Both are said in a file of the build, and a build is not something the engine owns - so switching this on is
## what says them.
##
## This is written in GDScript rather than in C# on purpose. A plugin of C# is one the editor can only load out of an
## assembly which was built, and the build of a project which asked for a singleton is one which cannot succeed before
## this file exists: it is a circle, and the editor has no way into it. What this does is plumbing of files, which needs
## no assembly at all.
##
## What is written is a `Directory.Build.props` at the root of the project, because MSBuild reads that file where it
## stands without the project naming it. The project file of a Godot project is written and rewritten by the editor, so
## a line put into it is a line the next thing the editor does may take away; a file the build reads by itself is one
## nothing else has to know about.
##
## It is not taken away again, and that is deliberate. The only message which could take it away is `_exit_tree`, and
## the editor sends that when the plugin is switched off *and* when the editor is closing - so a file removed on the way
## out is one a build outside the editor, a command line or a runner, no longer finds. A project which wants the wiring
## gone deletes the file; the name of it is the one below.

const MARKER := "written by the Singleton addon"
const FILE_NAME := "Directory.Build.props"

func _enter_tree() -> void:
	var script: Script = get_script()
	var folder: String = script.resource_path.get_base_dir()
	var relative: String = folder.trim_prefix("res://")

	var props: String = relative.path_join("Singleton.Godot.props")
	var targets: String = relative.path_join("Singleton.Godot.targets")
	var path: String = "res://" + FILE_NAME

	var written := """<Project>

  <!-- %s -->
  <!--
    What a build has to be told about rather than the engine: the generator, which the compiler is given, and the
    weaver, which is run once the assembly was written. Both are named beside this file, in the folder the addon was
    installed into, so this file is what makes an addon install one - the project file itself is left alone, and the
    editor which rewrites that one is not fought with.

    This was written by the addon when it was switched on. A project which needs something of its own here should copy
    these two imports into the file it keeps, and the addon will then leave that one alone.
  -->

  <Import Project="%s" />
  <Import Project="%s" />

</Project>
""" % [MARKER, props, targets]

	if FileAccess.file_exists(path):
		var held := FileAccess.get_file_as_string(path)
		if held.contains(MARKER):
			# Already ours, which is what the second time a project is opened looks like rather than a fault.
			return

		push_warning(
			"%s already exists and was not written by Singleton, so it was left as it is. " % FILE_NAME
			+ "Add these two lines to it:\n  <Import Project=\"%s\" />\n  <Import Project=\"%s\" />" % [props, targets]
		)
		return

	var file := FileAccess.open(path, FileAccess.WRITE)
	if file == null:
		push_error("Singleton: could not write %s, so the generator and the weaver are not wired." % FILE_NAME)
		return

	file.store_string(written)
	file.close()
	print("Singleton: wrote %s, which hands the generator and the weaver to the build." % FILE_NAME)
