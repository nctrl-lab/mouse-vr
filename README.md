# mouse-vr

## Summary

This branch is forked from [janelia-unity-toolkit](https://github.com/JaneliaSciComp/janelia-unity-toolkit) with additional functionality for rodent virtual reality experiment.  

## Installation summary

1. Install [Unity Hub](https://unity.com/download)
2. Install Unity 2020.3 LTS from Unity Hub
3. Download [this github](https://github.com/lapis42/mouse-vr)
4. Install [org.janelia.package-installer](https://github.com/lapis42/mouse-vr/tree/master/org.janelia.package-installer) package
5. Install [org.janelia.mouse-vr](https://github.com/lapis42/mouse-vr/tree/master/org.janelia.mouse-vr)
6. Install [Bezier Path Creator](https://assetstore.unity.com/packages/tools/utilities/b-zier-path-creator-136082) from Asset Store
7. Run "mouse-vr" package from the Unity editor's "Window" menu

## Installation

Use Unity's approach for [installing a local package](https://docs.unity3d.com/Manual/upm-ui-local.html) saved outside a Unity project.  Start by cloning this repository into a directory (folder) on your computer; it is simplest to use a directory outside the directories of any Unity projects.  Note that once a Unity project starts using a package from this directory, the project automatically gets any updates due to a Git pull in the directory.

Some of the packages in this repository depend on other packages (e.g., the [package for collision handling](https://github.com/JaneliaSciComp/janelia-unity-toolkit/tree/master/org.janelia.collision-handling) uses the [package for logging](https://github.com/JaneliaSciComp/janelia-unity-toolkit/tree/master/org.janelia.logging)), but the standard [Unity Package Manager window](https://docs.unity3d.com/Manual/upm-ui.html) does not automatically install such dependencies.  To overcome this weakness, use the [org.janelia.package-installer](https://github.com/JaneliaSciComp/janelia-unity-toolkit/tree/master/org.janelia.package-installer) package.  Once it is installed (as described in the next subsection), use it to install [mouse-vr] package as follows:

1. Choose "Install Package and Dependencies" from the Unity editor's "Window" menu.
2. In the file chooser that appears, navigate to the directory with the [mouse-vr](https://github.com/lapis42/mouse-vr) repository. Then go down one more level into the subdirectory for the particular package to be installed (e.g., `org.janelia.mouse-vr`).
3. Select the `package.json` file and press the "Open" button (or double click).
4. A window appears, listing the package and other packages on which it depends.  Any dependent package already installed appears in parentheses.
5. Press the "Install" button to install all the listed packages in order.
6. All the listed packages now should be active.  They should appear in the Unity editor's "Project" tab, in the "Packages" section, and also should appear in the editor's Package Manager window, launched from the "Package Manger" item in the editor's "Window" menu.

Note that `org.janelia.package-installer` requires Unity version 2019.3.0 or later.
