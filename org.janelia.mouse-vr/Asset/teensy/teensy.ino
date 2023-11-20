const unsigned long airDuration = 200;  // 200 ms
const unsigned long intervalDuration = 300; // 300 ms
const unsigned long finishDuration = 0; // infinite if zero
unsigned long now;
unsigned long targetTime = 0;
unsigned long startTime = 0;


#define STANDBY 0
#define ON 1
#define OFF 2
#define DONE 3
int airState = STANDBY;
const int airPin = 0;

void setup() {
    Serial.begin(115200);
    pinMode(airPin, OUTPUT);
    digitalWriteFast(airPin, LOW);
}

void loop() {
    now = millis();
    checkSerial();
    checkAir();
}

void checkSerial() {
    if (Serial.available()) {
    	  char cmd = Serial.read();
    	
        if (cmd == 'p') {
              if (airState == STANDBY  || airState == DONE) {
                airState = ON;
                startTime = now;
                targetTime = now;
                digitalWriteFast(airPin, HIGH);
                Serial.println("Air start");
              }
        }
        else if (cmd == '0') {
            airState = STANDBY;
            digitalWriteFast(airPin, LOW);
            Serial.println("Done");
        }
    }
}

void checkAir() {
    if (airState == ON) {
        if (finishDuration > 0 && now - finishDuration >= startTime) {
    	    airState = DONE;
    	    digitalWriteFast(airPin, LOW);
    	}    	
        else if (now - airDuration >= targetTime) {
            airState = OFF;
            targetTime = now;
            digitalWriteFast(airPin, LOW);
        }

    }
    else if (airState == OFF) {
        if (now - intervalDuration >= targetTime) {
            airState = ON;
            targetTime = now;
            digitalWrite(airPin, HIGH);
        }
    }
}
