# mouse-vr

## Summary

This branch is forked from [janelia-unity-toolkit](https://github.com/JaneliaSciComp/janelia-unity-toolkit) with additional functionality for rodent virtual reality experiment.  

## Installation

Use Unity's approach for [installing a local package](https://docs.unity3d.com/Manual/upm-ui-local.html) saved outside a Unity project.  Start by cloning this repository into a directory (folder) on your computer; it is simplest to use a directory outside the directories of any Unity projects.  Note that once a Unity project starts using a package from this directory, the project automatically gets any updates due to a Git pull in the directory.

Some of the packages in this repository depend on other packages (e.g., the [package for collision handling](https://github.com/JaneliaSciComp/janelia-unity-toolkit/tree/master/org.janelia.collision-handling) uses the [package for logging](https://github.com/JaneliaSciComp/janelia-unity-toolkit/tree/master/org.janelia.logging)), but the standard [Unity Package Manager window](https://docs.unity3d.com/Manual/upm-ui.html) does not automatically install such dependencies.  To overcome this weakness, use the [org.janelia.package-installer](https://github.com/JaneliaSciComp/janelia-unity-toolkit/tree/master/org.janelia.package-installer) package.  Once it is installed (as described in the next subsection), use it to install [mouse-vr] package as follows:

1. Choose "Install Package and Dependencies" from the Unity editor's "Window" menu.
2. In the file chooser that appears, navigate to the directory with the [janelia-unity-toolkit](https://github.com/JaneliaSciComp/janelia-unity-toolkit) repository. Then go down one more level into the subdirectory for the particular package to be installed (e.g., `org.janelia.collision-handling`).
3. Select the `package.json` file and press the "Open" button (or double click).
4. A window appears, listing the package and other packages on which it depends.  Any dependent package already installed appears in parentheses.
5. Press the "Install" button to install all the listed packages in order.
6. All the listed packages now should be active.  They should appear in the Unity editor's "Project" tab, in the "Packages" section, and also should appear in the editor's Package Manager window, launched from the "Package Manger" item in the editor's "Window" menu.

This system also can install multiple packages (and their dependencies) at once.  In step 3, above, choose a file like `manifest.json` that contains a list of package names:
```
[
    "org.janelia.setup-camera-n-gon",
    "org.janelia.fictrac-collision"
]
```
Each of the listed packages (and their dependencies) will be loaded in turn, as if they were chosen individually in step 3.  See the documentation on [org.janelia.package-installer](https://github.com/JaneliaSciComp/janelia-unity-toolkit/tree/master/org.janelia.package-installer) for more details.

Note that `org.janelia.package-installer` requires Unity version 2019.3.0 or later.
