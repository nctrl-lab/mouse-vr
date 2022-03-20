# Janelia Mouse Treadmill Support

## Summary

This package (org.janelia.mouse-treadmill) supports the use in Unity of [rodent spherical treadmill system](https://sphericaltreadmill.com), a modified system from [FlyFizz](https://wiki.janelia.org/wiki/display/flyfizz/Home) for tracking the motion of an animal walking on a ball.  

The device communicates to the host computer through USB ports using [FTDI D2XX interface](https://ftdichip.com/drivers/d2xx-drivers/).


## Installation

Follow the [installation instructions in the main repository](https://github.com/JaneliaSciComp/janelia-unity-toolkit/blob/master/README.md#installation) to install this package and its dependency, [org.janelia.io](https://github.com/JaneliaSciComp/janelia-unity-toolkit/tree/master/org.janelia.io) and [org.janelia.logging](https://github.com/JaneliaSciComp/janelia-unity-toolkit/tree/master/org.janelia.logging).  


## Calibration

To use the output of the ball tracker in a Unity application, there must be a calibration scale to map the ball tracker's native measurements (the raw pixel displacements of `x0`, `y0`, `x1`, `y1`) to Unity distance units.  By default, the code has a calibration scale that should be about right for a ball with 16-inch diameter and Unity units where 1 unit is 10 cm (0.1 m).  