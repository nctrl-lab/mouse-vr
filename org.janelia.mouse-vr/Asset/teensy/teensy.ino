const unsigned long airDuration = 200;  // 200 ms
const unsigned long intervalDuration = 300; // 300 ms
const unsigned long finishDuration = 0; // infinite if zero
unsigned long now;
unsigned long targetTime = 0;
unsigned long startTime = 0;

#define STANDBY 0
#define AIRON 1
#define AIROFF 2
#define DONE 3
int state = STANDBY;
const int outputPin = 0;

void setup() {
    Serial.begin(115200);
    pinMode(outputPin, OUTPUT);
    digitalWriteFast(outputPin, LOW);
}

void loop() {
    now = millis();
    checkSerial();
    checkAir();
}

void checkSerial() {
    if (Serial.available()) {
    	char cmd = Serial.read();
    	
    	if (cmd == 'p'){
            if(state == STANDBY  || state == DONE) {
               Serial.println("Go");
               state = AIRON;
               startTime = now;
               targetTime = now;
               digitalWriteFast(outputPin, HIGH);
            }
        }
        else if (cmd == '0'){
            Serial.println("Done");
            state = STANDBY;
            digitalWriteFast(outputPin, LOW);            
        }
    }
}

void checkAir() {
    if (state == AIRON) {
        if (finishDuration > 0 && now - finishDuration >= startTime) {
    	    state = DONE;
    	    digitalWriteFast(outputPin, LOW);
    	}    	
        else if (now - airDuration >= targetTime) {
            state = AIROFF;
            targetTime = now;
            digitalWriteFast(outputPin, LOW);
        }

    }
    else if (state == AIROFF) {
        if (now - intervalDuration >= targetTime) {
            state = AIRON;
            targetTime = now;
            digitalWrite(outputPin, HIGH);
        }
    }
}
