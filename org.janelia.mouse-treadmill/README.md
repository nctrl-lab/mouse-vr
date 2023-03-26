# Janelia Mouse Treadmill Support

## Summary

This package (org.janelia.mouse-treadmill) supports the use in Unity of [rodent spherical treadmill system](https://sphericaltreadmill.com), a modified system from [FlyFizz](https://wiki.janelia.org/wiki/display/flyfizz/Home) for tracking the motion of an animal walking on a ball.  

The device communicates to the host computer through USB ports using [FTDI D2XX interface](https://ftdichip.com/drivers/d2xx-drivers/). Also check [C# examples](https://ftdichip.com/software-examples/code-examples/csharp-examples/)


## Installation

Follow the [installation instructions in the main repository](https://github.com/JaneliaSciComp/janelia-unity-toolkit/blob/master/README.md#installation) to install this package and its dependency, [org.janelia.io](https://github.com/JaneliaSciComp/janelia-unity-toolkit/tree/master/org.janelia.io) and [org.janelia.logging](https://github.com/JaneliaSciComp/janelia-unity-toolkit/tree/master/org.janelia.logging).  

Attach ExampleController.cs on the Unity Object.  


## Calibration

To use the output of the ball tracker in a Unity application, there must be a calibration scale to map the ball tracker's native measurements (the raw pixel displacements of `x0`, `y0`, `x1`, `y1`) to Unity distance units.  By default, the code has a calibration scale that should be about right for a ball with 16-inch diameter and Unity units where 1 unit is 10 cm (0.1 m).  


## Description

- The system communicates using the FTDI D2xx Driver library.
- The manual says Virtual COM protocol can result defective communication.
- Baudrate: 1250000 bits per second
- Command:
  - [252 0]: dump internal register
      - This command returns 50 bytes read from registers in each camera
  - [254 0]: stop motion data
  - [255 0]: start motion data
      - This command initiates the 4kHz motion data from the cameras.
      - Each sample is 12 bytes.

- Packet design (12 bytes/packet):
  - 1: zero
  - 2: a counter from 1 to 255

  - 3: delta_x from camera 0 - zero centered on 128
  - 4: delta_y from camera 0 - zero centered on 128
  - 5: delta_x from camera 1 - zero centered on 128
  - 6: delta_y from camera 1 - zero centered on 128

  - 7: surface quality from camera 0 (higher the better / worst is 1)
  - 8: surface quality from camera 1

  - 9: high byte of shutter speed, camera 0
  - 10: low byte of shutter speed, camera 0
  - 11: high byte of shutter speed, camera 1
  - 12: low byte of shutter speed, camera 1
       --> shutterSpeed = ((HighByte -1)*256+lowByte) / 24MHz

- Packet speed: 4000 packets / second
- Ftdi is checked at 400 Hz (10 packets per read)

```cs
// example code
ser = SerialPort("COM3", 1250000);
ser.Open();
ser.Write(new byte[] {255, 0}, 0, 2); // this starts streaming
ser.Read(buffer, 0, read_n_byte); // read data
processData(buffer); // custom function that processes data
ser.Write(new byte[] {254, 0}, 0, 2); // this end streaming
ser.Close();
```

## Design


- Pseudocode for MouseTreadmillReader
```python
class MouseTreadmillReader:
    def Start() # starts FtdiReader that continously updates RingBuffer
    def Update(): # updates ball position
        return position, angle, treadmillLog
```



- Pseudocode for FtdiReader
```python
class FtdiReader:
    def Start(deviceIndex = 0)
    def Take(packets)
    def Clear()
    def OnDisable() # stops thread and close FTDI connection
```

* This uses org.janelia.io/Runtime/RingBuffer.cs RingBuffer class to store packets
- Pseudocode for RingBuffer
```python
class RingBuffer:
    def __init__(itemCount=400, itemSize=12*10)
    def Give(packets) # save 120 bytes
    def Take(packets) # take 120 bytes
    def Clear() # clear buffer
```
