// Encoder2RDS
//
//  Read the encoder and translate to USB output that mimics the camera treadmill system
//  Teensy 3.2 Arduino 1.8.3 with Teensy Extensions
//
//  Encoder A - pin 0
//  Encoder B - pin 1
//  Encoder VCC - Vin
//  Encoder ground - GND
//
//  The data packet we need to put out consists of:
//  0           : marks start of data pack - no other data can be = 0
//  sampleCount : A counter that counts from 1 to 255 and then loops back to 1
//  x[0]        : counts since last report, 0x80 to 0x7f, offset by adding 0x80
//  y[0]         
//  x[1]        
//  y[1]        

//
// Steve Sawtelle
// 20180420
// jET Janelia HHMI 

#define VERSION "20231106"
// ===== VERSIONS ======
// 20231106 Dohoung Kim
// - now using ENAB512-P (Dream Solution, Korea)
// - 512 ppr

// 20230826 Dohoung Kim
// - using HEDR-542x encoder (200 ppr)
// - disc radius: 95 mm
// - mmPerCount: 190*pi / 200 = 2.985
// - now it tries to mimic pixart system which uses 6 byte packet
//
// 20220321 Dohoung Kim
// - remove analog output
// - using different encoder: TRD-MX1000AD (1000 ppr)
// - disc radius: ~60 mm
// - mmPerCount: 120*pi / 1000 = 0.377

// 20180529 sws
// - only send speed (not DAC value) when using 'S' command

// 20180523 sws
// - add in check for reverse dir in encoder int and swap dir sense if needed

// 20180518 sws
// - add in EEPROM cal values
// - add in analog out and direction
// - add help and debug commands

// 201804254 sws 
//  - started

#define FRAMECLK 250     // usec per frame (4 kHz)
IntervalTimer frameTimer;

#define MOTION_STOP  254    // Stop Data Acquisition
#define MOTION_START 255    // Start Data Acquisition

#define encAPin 0
#define encBPin 1
#define dirPin 2

volatile boolean sampling = false;
volatile boolean dir = false; // false: forward, true: backward

volatile uint8_t ycounts = 128;
volatile uint8_t samples = 1;
uint8_t outStream[] = {0, 1, 128, 128, 128, 128};

// -------------------------------------------
// interrupt routine for ENCODER_A rising edge
// -------------------------------------------
void encoderInt()
{
    if (sampling) {
      int ENCA = digitalReadFast(encAPin);
      int ENCB = digitalReadFast(encBPin);

      if (dir) ENCB = !ENCB;  // if reverse direction, make it look backwards

      if (ENCA != ENCB)
      {   
        if (ycounts < 250) ycounts++;
      }
      else
      {
        if (ycounts > 1) ycounts--;
      }
    }
}

void frameSend()
{
   noInterrupts();
   uint8_t yval = ycounts;
   ycounts = 128;
   interrupts();

   if(samples == 0) samples++;
   outStream[1] = samples++;
   outStream[3] = yval;
   outStream[5] = yval;
   Serial.write(outStream, 6);
}

void setup()
{
  pinMode(13, OUTPUT);
  digitalWrite(13, LOW);

  Serial.begin(1250000);
  while(!Serial);
  
  pinMode(encAPin, INPUT_PULLUP); // sets the digital pin as input
  pinMode(encBPin, INPUT_PULLUP); // sets the digital pin as input
  pinMode(dirPin,  INPUT_PULLUP); // reverse direction if this one is connected to ground

  attachInterrupt(encAPin, encoderInt, RISING); // check encoder every A pin rising edge
}

void loop()
{
  if (Serial.available())
  {
    uint8_t cmdin = Serial.read();
    // we will just process everything - only 254 and 255 matter, parameters (0,1) will be processed as commands and ignored
    switch(cmdin)
    {
     case '?':
         Serial.print("RDS interface with Speed and Direction Outputs V:");
         Serial.println(VERSION);
         Serial.println(" 'b' to start streaming");
         Serial.println(" 'e' to end streaming");
         break;
     case 255: // 'b': // //Start Data Acquisition
     case 'b':
        digitalWriteFast(13, HIGH);
        frameTimer.begin(frameSend, FRAMECLK);
        dir = boolean(digitalReadFast(dirPin));
        sampling = true;
        break;
     case 254: //'e': // //Stop Data Acquisition
     case 'e':
        digitalWriteFast(13, LOW);
        frameTimer.end();       
        sampling = false;
        ycounts = 128;
        samples = 1;
        break;
     default:
        break;
    } // end command switch
  } // end serial in
}
