// Original code from 
// Samokhin, A.S. Syringe Pump Created using 3D Printing Technology and Arduino Platform.
// J Anal Chem 75, 416–421 (2020). https://doi.org/10.1134/S1061934820030156

#include <LiquidCrystal.h>
#include <EEPROM.h>



/********************************************************************
          BASIC SETTINGS
********************************************************************/

// Stepper motor
#define NOFMICROSTEPS 32      // The number of microsteps per step
#define NOFSTEPSPER360 200    // The number of steps per revolution
#define MAXRPM 240            // Maximum RPM (rotations per minute)
#define INVERTDIRECTION true // If the carriage moves in the opposite direction, change to 'false'

// Leadscrew
#define MMPER360 2            // Leadscrew pitch, mm per revolution

// Arduino pins
#define STEP_PIN 2            // STEP pin (A4988)
#define DIRECTION_PIN 3       // DIR pin (A4988)
#define ENABLE_PIN 12         // EN pin (A4988)
#define ENDSTOP_PIN1 A1       // Endstop pin (forward movement)
#define ENDSTOP_PIN2 A2       // Endstop pin (backward movement)
// #define BUZZER_PIN 11         // Buzzer pin
#define BUTTONS_PIN A0        // Button pin (lcd keypad shield)
#define INPUT_PIN 11

/********************************************************************/



#define DEBOUNCEDELAY 50
#define LONGPRESSDELAY 450
#define AUTOCHANGINGDELAY 300

#define NITEMS 43

#define LINK2S 12
#define LINK2E 13
#define LINK2V 20
#define LINK2T 21
#define LINK2F 22
#define LINK2DIA 41
#define LINK2LPV 42

LiquidCrystal lcd(8, 9, 4, 5, 6, 7);

struct MenuItem
{
  uint8_t button[4]; // right, up, down, left
  uint8_t type;
  char *text;
  uint8_t address;
  uint32_t value;
};

enum listOfButtons {RIGHT, UP, DOWN, LEFT, SELECT, NA};
enum listOfButtonStates {RELEASE, SHORTPRESS, MEDIUMPRESS, LONGPRESS};
enum listOfPrograms {CHANGEVALUE = 200, SELECTFROMLIST, INFUSEVOLUME, INFUSETIME, REFILLVOLUME, REFILLFULL, CYCLEMODE, FIRMWAREINFO};
enum listOfItemTypes {TEXT, LIST, VOLUME, TIME, FLOWRATE, LENGTHPERVOLUME, DIAMETER};

const char firmwareInfo[] = "1.0";
const uint16_t volumeDivider[2] = {1000, 1};
const uint16_t timeDivider[3] = {1, 60, 3600};
const uint16_t flowrateDivider[4] = {60000, 1000, 60, 1};
const float stepsPERmm = NOFSTEPSPER360 / MMPER360;

MenuItem items[NITEMS];
uint8_t lastButton = NA;
uint8_t buttonState = RELEASE;
uint32_t buttonPressTime = 0;
uint8_t currentItem = 1;
float coef;
uint8_t endStopPin;
volatile uint32_t ustepCounter;
uint32_t ustepCounterLimit;

uint32_t getValue(uint8_t itemNo);
void changeValue();
void setValue(uint8_t itemNo, uint8_t digits[8]);
void selectFromList();
void calculateActualValue(uint8_t itemNo);
void recalculateSyringe(uint8_t itemNo);
void printScreen();
void printUnits();
void printFirmwareInfo();
void printTimeAndVolume(uint16_t pumpingTime, uint32_t pumpingVolume);
void showScreensaver();
void readFromEEPROM(uint8_t itemNo);
void writeToEEPROM(uint8_t itemNo);
void getButtonState();
uint16_t valueDivider (uint8_t n);
uint8_t getElementNo(uint8_t linkToList);
void waitingForButton(uint8_t buttonCode, uint8_t itemNo);
//void beep (uint16_t delayOn = 15, uint16_t delayOff = 100);
bool checkEndstop(int8_t pumpingDirection);
void infuseVolume();
void infuseTime();
void refillVolume();
void refillFull();
void pumpSingly(int8_t pumpingDirection, uint32_t flowrate, uint32_t volume);
void pumpContinuously();
uint8_t pump(uint32_t flowrate, uint32_t volume);

enum listOfStates {STANDBY, INFUSING, FINISHED};
int inputState = STANDBY;


/********************************************************************
  setup
********************************************************************/
void setup()
{
  lcd.begin(16, 2);

  pinMode(STEP_PIN, OUTPUT);
  pinMode(DIRECTION_PIN, OUTPUT);
  pinMode(ENABLE_PIN, OUTPUT);
  pinMode(ENDSTOP_PIN1, INPUT_PULLUP);
  pinMode(ENDSTOP_PIN2, INPUT_PULLUP);
  pinMode(INPUT_PIN, INPUT_PULLUP);

  digitalWrite(ENABLE_PIN, HIGH); // diabled when HIGH

  // right, up, down, left
  items[1]  = {5, 0, 2, 0, TEXT, "Infuse"};
  items[2]  = {7, 1, 3, 0, TEXT, "Refill"};
  items[3]  = {9, 2, 4, 0, TEXT, "Cycle Mode"};
  items[4]  = {10, 3, 0, 0, TEXT, "Settings"};
  items[5]  = {15, 0, 6, 1, TEXT, "Target Volume"};
  items[6]  = {16, 5, 0, 1, TEXT, "Target Time"};
  items[7]  = {17, 0, 8, 2, TEXT, "Full Refill"};
  items[8]  = {18, 7, 0, 2, TEXT, "Target Volume"};
  items[9]  = {19, 0, CHANGEVALUE, 3, FLOWRATE, "infuse flow:", 1, 120000000};
  items[10] = {20, 0, 11, 4, TEXT, "Units"};
  items[11] = {23, 10, 12, 4, TEXT, "Syringe"};
  items[12] = {25, 11, 13, 4, LIST, "Sound", 2};
  items[13] = {27, 12, 14, 4, LIST, "EEPROM", 3};
  items[14] = {FIRMWAREINFO, 13, 0, 4, TEXT, "Firmware"};
  items[15] = {29, 0, CHANGEVALUE, 5, VOLUME, "volume:", 4, 1000000};
  items[16] = {30, 0, CHANGEVALUE, 6, TIME, "time:", 5, 60000};
  items[17] = {REFILLFULL, 0, CHANGEVALUE, 7, FLOWRATE, "flowrate:", 6, 2000000};
  items[18] = {31, 0, CHANGEVALUE, 8, VOLUME, "volume:", 7, 1000000};
  items[19] = {CYCLEMODE, 0, CHANGEVALUE, 9, FLOWRATE, "refill flow:", 8, 2000000};
  items[20] = {32, 0, 21, 10, LIST, "Volume Units", 9};
  items[21] = {34, 20, 22, 10, LIST, "Time Units", 10};
  items[22] = {37, 21, 0, 10, LIST, "Flowrate Units", 11};
  items[23] = {41, 0, 24, 11, TEXT, "Diameter"};
  items[24] = {42, 23, 0, 11, TEXT, "Length/Volume"};
  items[25] = {SELECTFROMLIST, 0, 26, 12, TEXT, "on"};
  items[26] = {SELECTFROMLIST, 25, 0, 12, TEXT, "off"};
  items[27] = {SELECTFROMLIST, 0, 28, 13, TEXT, "on"};
  items[28] = {SELECTFROMLIST, 27, 0, 13, TEXT, "off"};
  items[29] = {INFUSEVOLUME, 0, CHANGEVALUE, 15, FLOWRATE, "flowrate:", 12, 2000000};
  items[30] = {INFUSETIME, 0, CHANGEVALUE, 16, FLOWRATE, "flowrate:", 13, 2000000};
  items[31] = {REFILLVOLUME, 0, CHANGEVALUE, 18, FLOWRATE, "flowrate:", 14, 2000000};
  items[32] = {SELECTFROMLIST, 0, 33, 20, TEXT, "mL"};
  items[33] = {SELECTFROMLIST, 32, 0, 20, TEXT, "uL"};
  items[34] = {SELECTFROMLIST, 0, 35, 21, TEXT, "s"};
  items[35] = {SELECTFROMLIST, 34, 36, 21, TEXT, "min"};
  items[36] = {SELECTFROMLIST, 35, 0, 21, TEXT, "h"};
  items[37] = {SELECTFROMLIST, 0, 38, 22, TEXT, "mL/s"};
  items[38] = {SELECTFROMLIST, 37, 39, 22, TEXT, "mL/min"};
  items[39] = {SELECTFROMLIST, 38, 40, 22, TEXT, "uL/s"};
  items[40] = {SELECTFROMLIST, 39, 0, 22, TEXT, "uL/min"};
  items[41] = {0, 0, CHANGEVALUE, 23, DIAMETER, "diameter:", 15, 9830};
  items[42] = {0, 0, CHANGEVALUE, 24, LENGTHPERVOLUME, "length/volume:", 16, 13177};
  // 2 or 3 mL Syringe
  // 13.177 mm/mL = 9.830 diameter
  //
  // 1 mL syringe
  // 7 mm/mL = 

  for (uint8_t i = 1; i < NITEMS; i++)
  {
    if (items[i].type != TEXT) readFromEEPROM(i);
  }

  recalculateSyringe(LINK2DIA);
  //beep();
  showScreensaver();
}



/********************************************************************
  loop
********************************************************************/
void loop()
{
  uint8_t nextItem;
  getButtonState(); // this changes buttonState and lastButton
  if (buttonState == SHORTPRESS && lastButton != SELECT)
  {
    buttonState = MEDIUMPRESS; // pressed is now processed

    nextItem = items[currentItem].button[lastButton];
    if (nextItem != 0 && nextItem < 200)
    {
      currentItem = nextItem;
      printScreen();
    }
    else if (nextItem == CHANGEVALUE) changeValue();
    else if (nextItem == SELECTFROMLIST) selectFromList();
    else if (nextItem == INFUSEVOLUME) infuseVolume();
    else if (nextItem == INFUSETIME) infuseTime();
    else if (nextItem == REFILLVOLUME) refillVolume();
    else if (nextItem == REFILLFULL) refillFull();
    else if (nextItem == CYCLEMODE) pumpContinuously();
    else if (nextItem == FIRMWAREINFO) printFirmwareInfo();
  }

  checkInput();

}


// check external input by pin 11
void checkInput()
{
  if (inputState == STANDBY && digitalRead(INPUT_PIN) == 0)
  {
    inputState = INFUSING;
    infuseVolume();
  }
  else if (inputState == FINISHED && digitalRead(INPUT_PIN) == 1)
  {
    inputState = STANDBY;
  }
}


/********************************************************************
  getValue
********************************************************************/
uint32_t getValue(uint8_t itemNo)
{
  uint16_t divider = valueDivider(items[itemNo].type);
  uint8_t lastDigitIncrement = 0;
  if ( uint16_t(items[itemNo].value % divider) > divider / 2) lastDigitIncrement = 1;

  if (items[itemNo].value / divider > 99999999)
  {
    items[itemNo].value = divider * 99999999;
    return 99999999;
  }
  else return (items[itemNo].value / divider) + lastDigitIncrement;
}



/********************************************************************
  changeValue
********************************************************************/
void changeValue()
{
  uint8_t digits[8];
  uint32_t value = getValue(currentItem);
  for (int8_t i = 7; i >= 0; i--)
  {
    digits[i] = uint8_t(value % 10);
    value /= 10;
  }

  lcd.setCursor(0, 1);
  for (int8_t i = 0; i < 4; i++) lcd.print(digits[i]);

  lcd.setCursor(0, 1);
  lcd.cursor();
  int8_t i = 0;
  while (i >= 0 && i < 8)
  {
    getButtonState();
    if (buttonState == SHORTPRESS || buttonState == LONGPRESS)
    {
      if (lastButton == UP) digits[i] = (digits[i] + 1) % 10;
      else if (lastButton == DOWN) digits[i] = (digits[i] + 9) % 10;
      lcd.print(digits[i]);

      if (lastButton == RIGHT) i++;
      else if (lastButton == LEFT) i--;

      if (i == -1 && buttonState != SHORTPRESS) i++;
      if (i == 8 && buttonState != SHORTPRESS) i--;

      if (i < 5)lcd.setCursor(i, 1);
      else lcd.setCursor(i + 1, 1);

      if (buttonState == SHORTPRESS) buttonState = MEDIUMPRESS;
      else if (buttonState == LONGPRESS)
      {
        uint32_t nextMillis = millis() + AUTOCHANGINGDELAY;
        while (nextMillis > millis() && buttonState == LONGPRESS) getButtonState();
      }
    }
  }

  if (i == 8)
  {
    bool isSame = true;
    value = getValue(currentItem);
    for (int8_t j = 7; j >= 0 && isSame == true; j--)
    {
      if (digits[j] != uint8_t(value % 10)) isSame = false;
      value /= 10;
    }

    bool isZero = true;
    for (int8_t j = 7; j >= 0 && isZero == true; j--)
    {
      if (digits[j] != 0) isZero = false;
    }

    if (isSame == false && isZero == false)
    {
      setValue(currentItem, digits);
      //beep();
    }
  }

  lcd.noCursor();
  lcd.setCursor(0, 1);
  printScreen();
}



/********************************************************************
  setValue
********************************************************************/
void setValue(uint8_t itemNo, uint8_t digits[8])
{
  uint32_t value = 0;
  for (int8_t i = 0; i < 8; i++) value = value * 10 + digits[i];

  uint16_t divider = valueDivider(items[itemNo].type);
  if (4294000000 / divider < value) items[itemNo].value = 4294000000;
  else items[itemNo].value = value * divider;

  if (itemNo == LINK2LPV || itemNo == LINK2DIA)
  {
    recalculateSyringe(itemNo);
    writeToEEPROM(LINK2LPV);
    writeToEEPROM(LINK2DIA);
  }
  else writeToEEPROM(itemNo);
}



/********************************************************************
  selectFromList
********************************************************************/
void selectFromList()
{
  uint8_t parentItem = items[currentItem].button[LEFT];
  items[parentItem].button[RIGHT] = currentItem;
  writeToEEPROM(parentItem);
  //beep();
  currentItem = parentItem;
  printScreen();
}



/********************************************************************
  calculateActualValue
********************************************************************/
void calculateActualValue(uint8_t itemNo)
{
  if (items[itemNo].type == FLOWRATE)
  {
    float maxFlowrate = 1e9 * MAXRPM * MMPER360 / (float)items[LINK2LPV].value;
    if ((float)(items[itemNo].value) > maxFlowrate) items[itemNo].value = (uint32_t)maxFlowrate;

    float ustepsPERmin = coef * NOFMICROSTEPS * (float)items[itemNo].value;
    if (ustepsPERmin < 14.4) ustepsPERmin = 14.4;
    float multiplier;
    if (ustepsPERmin >= 229) multiplier = 15000000;
    else multiplier = 937500;
    float tempFloat = multiplier / ustepsPERmin;
    uint16_t countsPERustepActual = (uint16_t)(tempFloat + 0.5);
    tempFloat = multiplier / (float)countsPERsutepActual;
    tempFloat /= (float)(coef * NOFMICROSTEPS);
    items[itemNo].value = (uint32_t)tempFloat;
  }

  else if (items[itemNo].type == VOLUME)
  {
    uint32_t nSteps = (uint32_t)(coef * (float)items[itemNo].value + 0.5);
    float tempFloat = (float)nSteps / coef;
    items[itemNo].value = (uint32_t)tempFloat;
  }

  else if (items[itemNo].type == TIME)
  {
    if (items[itemNo].value > 59994000) items[itemNo].value = 59994000;
    items[itemNo].value -= (items[itemNo].value % 1000);
  }
}



/********************************************************************
  recalculateSyringe
********************************************************************/
void recalculateSyringe(uint8_t itemNo)
{
  if (itemNo == LINK2LPV)
  {
    float temp = 1128379 / sqrt((float)items[LINK2LPV].value);
    items[LINK2DIA].value = uint32_t(temp + 0.5);
  }
  else if (itemNo == LINK2DIA)
  {
    float temp = 1128379 / (float)items[LINK2DIA].value;
    temp *= temp;
    items[LINK2LPV].value = uint32_t(temp + 0.5);
  }

  coef = ((float)items[LINK2LPV].value * stepsPERmm) * 1e-9;
  // steps/volume: steps/ml * 1e-6 = steps/ul * 1e-3

}



/********************************************************************
  printScreen
********************************************************************/
void printScreen()
{
  lcd.clear();
  lcd.setCursor(0, 0);

  if (items[currentItem].type == TEXT || items[currentItem].type == LIST)
  {
    lcd.print(F("> "));
    lcd.print(items[currentItem].text);

    if (items[currentItem].button[DOWN] != 0 && items[currentItem].button[DOWN] < 200)
    {
      lcd.setCursor(0, 1);
      lcd.print(F("  "));
      lcd.print(items[items[currentItem].button[DOWN]].text);
    }
  }

  else
  {
    calculateActualValue(currentItem);
    uint32_t value = getValue(currentItem);
    char outputLine[10] = "    0.000";
    for (int8_t i = 8; i >= 0 && value > 0; i--)
    {
      if (i != 5)
      {
        outputLine[i] = char((value % 10) + '0');
        value /= 10;
      }
    }

    lcd.print(items[currentItem].text);
    lcd.setCursor(0, 1);
    lcd.print(outputLine);
    printUnits();
  }
}



/********************************************************************
  printUnits
********************************************************************/
void printUnits()
{
  lcd.print(F(" "));
  if (items[currentItem].type == VOLUME) lcd.print(items[items[LINK2V].button[RIGHT]].text);
  else if (items[currentItem].type == TIME) lcd.print(items[items[LINK2T].button[RIGHT]].text);
  else if (items[currentItem].type == FLOWRATE) lcd.print(items[items[LINK2F].button[RIGHT]].text);
  else if (items[currentItem].type == LENGTHPERVOLUME)
  {
    lcd.print(F("mm/"));
    lcd.print(items[items[LINK2V].button[RIGHT]].text);
  }
  else if (items[currentItem].type == DIAMETER) lcd.print(F("mm"));
}



/********************************************************************
  printFirmwareInfo
********************************************************************/
void printFirmwareInfo()
{
  lcd.clear();
  lcd.setCursor(0, 0);
  lcd.print(F("version:"));
  lcd.setCursor(0, 1);
  lcd.print(F("  "));
  lcd.print(firmwareInfo);

  waitingForButton(LEFT, currentItem);
}



/********************************************************************
  printTimeAndVolume
********************************************************************/
void printTimeAndVolume(uint16_t pumpingTime, uint32_t pumpingVolume)
{
  char outputLine[17] = "  0:00    0000xL";
  outputLine[14] = items[items[LINK2V].button[RIGHT]].text[0];

  uint8_t pointLocation;
  if (pumpingVolume < 100000)pointLocation = 3;
  else if (pumpingVolume < 1000000)
  {
    pointLocation = 4;
    pumpingVolume /= 10;
  }
  else
  {
    pointLocation = 6;
    pumpingVolume /= 100;
  }
  if (pointLocation < 6)outputLine[pointLocation + 8] = '.';
  uint16_t timeDivider;
  for (int8_t i = 5; i >= 0; i--)
  {
    if (pumpingTime > 0 && i != 3)
    {
      if (i == 4) timeDivider = 6;
      else timeDivider = 10;
      outputLine[i] = uint8_t(pumpingTime % timeDivider) + '0';
      pumpingTime /= timeDivider;
    }

    if (pumpingVolume > 0 && i != pointLocation)
    {
      outputLine[i + 8] = uint8_t(pumpingVolume % 10) + '0';
      pumpingVolume /= 10;
    }
  }

  lcd.setCursor(0, 1);
  lcd.print(outputLine);
}



/********************************************************************
  showScreensaver
********************************************************************/
void showScreensaver()
{
  lcd.clear();
  lcd.setCursor(0, 0);
  lcd.print(F("Laboratory of"));
  lcd.setCursor(0, 1);
  lcd.print(F("  Neural Control"));
  currentItem = 1;
  waitingForButton(NA, currentItem);
}



/********************************************************************
  getButtonState
********************************************************************/
void getButtonState()
{
  uint8_t button;

  uint8_t x = analogRead(BUTTONS_PIN) / 4; // this uses A0 pin
  if (x < 13) button = RIGHT;
  else if (x < 46) button = UP;
  else if (x < 84) button = DOWN;
  else if (x < 131) button = LEFT;
  else if (x < 207) button = SELECT;
  else button = NA; // not pressed

  if (button == NA) // not pressed
  {
    lastButton = NA;
    buttonState = RELEASE;
  }
  else if (button != lastButton) // state change -> starts to measure the press duration
  {
    lastButton = button;
    buttonState = RELEASE;
    buttonPressTime = millis();
  }
  else if (millis() - buttonPressTime > LONGPRESSDELAY) buttonState = LONGPRESS; // longer than 0.45 seconds
  else if (millis() - buttonPressTime > DEBOUNCEDELAY && buttonState == RELEASE) buttonState = SHORTPRESS; // longer than 0.05 seconds
}



/********************************************************************
  valueDivider
********************************************************************/
uint16_t valueDivider (uint8_t itemType)
{
  if (itemType == VOLUME) return volumeDivider[getElementNo(LINK2V)];
  else if (itemType == TIME) return timeDivider[getElementNo(LINK2T)];
  else if (itemType == FLOWRATE) return flowrateDivider[getElementNo(LINK2F)];
  else if (itemType == LENGTHPERVOLUME) return 1000 / volumeDivider[getElementNo(LINK2V)];
  else if (itemType == DIAMETER) return 1;
}



/********************************************************************
  getElementNo
********************************************************************/
uint8_t getElementNo(uint8_t linkToList)
{
  uint8_t uppermostItem = items[linkToList].button[RIGHT];
  uint8_t elementNo = 0;
  while (items[uppermostItem].button[UP] != 0)
  {
    uppermostItem = items[uppermostItem].button[UP];
    elementNo++;
  }
  return elementNo;
}



/********************************************************************
  waitingForButton
********************************************************************/
void waitingForButton(uint8_t button, uint8_t itemNo)
{
  if (button == NA) while (buttonState != SHORTPRESS) getButtonState();
  else while (buttonState != SHORTPRESS || lastButton != button) getButtonState();
  buttonState = MEDIUMPRESS;
  currentItem = itemNo;
  printScreen();
}



/********************************************************************
  beep
********************************************************************/
//void beep (uint16_t delayOn, uint16_t delayOff)
//{
//  uint8_t daughterItem = items[LINK2S].button[RIGHT];
//  if (items[daughterItem].text == "on")
//  {
//    digitalWrite(BUZZER_PIN, HIGH);
//    delay(delayOn);
//    digitalWrite(BUZZER_PIN, LOW);
//    delay(delayOff);
//  }
//}



/********************************************************************
  checkEndstop
********************************************************************/
bool checkEndstop(int8_t pumpingDirection)
{
  bool canMove = true;
  if (digitalRead(ENDSTOP_PIN1) == LOW && pumpingDirection > 0) canMove = false;
  else if (digitalRead(ENDSTOP_PIN2) == LOW && pumpingDirection < 0) canMove = false;

  if (canMove == false)
  {
    lcd.clear();
    lcd.setCursor(0, 0);
    if (pumpingDirection > 0) lcd.print(F("SYRINGE IS EMPTY"));
    else if (pumpingDirection < 0) lcd.print(F("SYRINGE IS FULL"));
    //beep(300);
    waitingForButton(LEFT, 1);
    return false;
  }
  else return true;
}



/********************************************************************
  readFromEEPROM
********************************************************************/
void readFromEEPROM(uint8_t itemNo)
{
  if (items[itemNo].type == LIST)
  {
    uint8_t temp;
    EEPROM.get(4 * items[itemNo].address, temp);
    if (temp != 0 && temp + 1 != 0)
    {
      if (items[temp].button[LEFT] == itemNo) items[itemNo].button[RIGHT] = temp;
    }
  }
  else
  {
    uint32_t temp;
    EEPROM.get(4 * items[itemNo].address, temp);
    if (temp != 0 && temp + 1 != 0) items[itemNo].value = temp;
  }
}



/********************************************************************
  writeToEEPROM
********************************************************************/
void writeToEEPROM(uint8_t itemNo)
{
  uint8_t daughterItem = items[LINK2E].button[RIGHT];
  if (items[daughterItem].text == "on" || itemNo == LINK2E)
  {
    if (items[itemNo].type == LIST) EEPROM.put(4 * items[itemNo].address, items[itemNo].button[RIGHT]);
    else EEPROM.put(4 * items[itemNo].address, items[itemNo].value);
  }
}



/********************************************************************
  infuseVolume
********************************************************************/
void infuseVolume()
{
  int8_t pumpingDirection = 1;
  uint32_t flowrate = items[currentItem].value;
  uint32_t volume = items[items[currentItem].button[LEFT]].value;

  pumpSingly(pumpingDirection, flowrate, volume);
}



/********************************************************************
  infuseTime
********************************************************************/
void infuseTime()
{
  int8_t pumpingDirection = 1;
  uint32_t pumpTime = items[items[currentItem].button[LEFT]].value;
  uint32_t flowrate = items[currentItem].value;
  float tempFloat = (float)flowrate * (float)pumpTime;
  tempFloat /= 60000;
  uint32_t volume;
  if (tempFloat > 4294000000) volume = 4294000000;
  else volume = (uint32_t)tempFloat;

  pumpSingly(pumpingDirection, flowrate, volume);
}



/********************************************************************
  refillVolume
********************************************************************/
void refillVolume()
{
  int8_t pumpingDirection = -1;
  uint32_t flowrate = items[currentItem].value;
  uint32_t volume = items[items[currentItem].button[LEFT]].value;

  pumpSingly(pumpingDirection, flowrate, volume);
}



/********************************************************************
  refillFull
********************************************************************/
void refillFull()
{
  int8_t pumpingDirection = -1;
  uint32_t flowrate = items[currentItem].value;
  uint32_t volume = 4294000000;

  pumpSingly(pumpingDirection, flowrate, volume);
}



/********************************************************************
  pumpSingly
********************************************************************/
void pumpSingly(int8_t pumpingDirection, uint32_t flowrate, uint32_t volume)
{
  if (checkEndstop(pumpingDirection) == true)
  {
    pump(pumpingDirection, flowrate, volume);
    //beep(300);
    waitingForButton(LEFT, 1);
  }
}



/********************************************************************
  pumpContinuously
********************************************************************/
void pumpContinuously()
{
  int8_t pumpingDirection[2] = {1, -1};
  uint32_t flowrate[2] = {items[items[currentItem].button[LEFT]].value, items[currentItem].value};
  uint32_t volume = 4294000000;
  uint8_t i = 0;

  while (pump(pumpingDirection[i], flowrate[i], volume) != 2)
  {
    i = 1 - i;
    //beep();
    delay(1000);
  }

  //beep(300);
  waitingForButton(LEFT, 1);
}



/********************************************************************
  pump
********************************************************************/
uint8_t pump(int8_t pumpingDirection, uint32_t flowrate, uint32_t volume)
{
  float tempFloat;

  // coef: steps per volume
  ustepCounterLimit = (uint32_t)(coef * volume + 0.5);
  ustepCounterLimit *= NOFMICROSTEPS;

 
  float ustepsPERmin = coef * NOFMICROSTEPS * (float)flowrate;
  if (ustepsPERmin < 14.4) ustepsPERmin = 14.4;
  float multiplier;
  if (ustepsPERmin >= 229) multiplier = 15000000;
  else multiplier = 937500;
  tempFloat = multiplier / ustepsPERmin;
  uint16_t countsPERustepActual = (uint16_t)(tempFloat + 0.5);
  float ustepsPERsActual = multiplier / (float)countsPERustepActual;
  ustepsPERsActual /= 60;

  lcd.clear();
  lcd.setCursor(0, 0);
  if (pumpingDirection > 0)
  {
    endStopPin = ENDSTOP_PIN1;
    if (INVERTDIRECTION == true) digitalWrite(DIRECTION_PIN, LOW);
    else digitalWrite(DIRECTION_PIN, HIGH);
    lcd.print(F(" >> INFUSING >> "));
  }
  else
  {
    endStopPin = ENDSTOP_PIN2;
    if (INVERTDIRECTION == true) digitalWrite(DIRECTION_PIN, HIGH);
    else digitalWrite(DIRECTION_PIN, LOW);
    lcd.print(F(" << REFILLING <<"));
  }
  lcd.setCursor(0, 1);
  printTimeAndVolume(0, 0);

  TCNT1 = 0;
  TCCR1A = 0;
  TCCR1B |= (1 << WGM12);
  if (ustepsPERmin >= 229)
  {
    TCCR1B &= ~( 1 << CS12);
    TCCR1B |= (1 << CS11);
    TCCR1B |= (1 << CS10);
  }
  else
  {
    TCCR1B |= (1 << CS12);
    TCCR1B &= ~( 1 << CS11);
    TCCR1B |= (1 << CS10);
  }

  digitalWrite(ENABLE_PIN, LOW);
  digitalWrite(STEP_PIN, LOW);

  ustepCounter = 1;
  uint32_t ustepCounterActual;
  float coef2 = ((float)items[LINK2LPV].value * stepsPERmm * 1e-9) * (float)valueDivider(VOLUME) * NOFMICROSTEPS * 10;
  bool isManualStop = false;
  uint32_t pumpingVolume;
  uint16_t pumpingTime = 1;
  uint32_t nextMillis = millis() + 1000;
  OCR1A = (countsPERustepActual - 1);
  TIMSK1 |= (1 << OCIE1A);
  while ( (TIMSK1 & (1 << OCIE1A)) )
  {
    // check button input while infusing
    getButtonState();
    if (buttonState == SHORTPRESS && lastButton == LEFT)
    {
      buttonState = MEDIUMPRESS;
      lcd.setCursor(0, 0);
      lcd.print(F("  STOP PUMPING? "));
      lcd.setCursor(0, 1);
      lcd.print(F("(YES)       (NO)"));

      // confirm stop infusing
      while ( (TIMSK1 & (1 << OCIE1A)) )
      {
        getButtonState();
        if (buttonState == SHORTPRESS)
        {
          buttonState = MEDIUMPRESS;
          if (lastButton == LEFT)
          {
            TIMSK1 &= ~( 1 << OCIE1A);
            isManualStop = true;
          }
          else if (lastButton == RIGHT)
          {
            lcd.setCursor(0, 0);
            if (pumpingDirection > 0) lcd.print(F(" >> INFUSING >> "));
            else lcd.print(F(" << REFILLING <<"));
            break;
          }
        }
      }
    }

    if (inputState == INFUSING && digitalRead(INPUT_PIN) == 1) {
        inputState = STANDBY;
        TIMSK1 &= ~( 1 << OCIE1A);
        isManualStop = true;
    }

    // print volume every second
    if (nextMillis <= millis())
    {
      cli();
      ustepCounterActual = ustepCounter;
      sei();

      pumpingVolume = (uint32_t)(((float)ustepCounterActual / coef2) + 0.5);
      printTimeAndVolume(pumpingTime, pumpingVolume);
      pumpingTime++;
      nextMillis += 1000;
    }
  }

  digitalWrite(ENABLE_PIN, HIGH);

  lcd.setCursor(0, 1);
  pumpingVolume = (uint32_t)(((float)ustepCounter / coef2) + 0.5);
  printTimeAndVolume(pumpingTime, pumpingVolume);


  lcd.setCursor(0, 0);
  if (isManualStop)
  {
    lcd.print(F("  MANUAL STOP   "));
    return 2;
  }
  if (ustepCounterLimit == ustepCounter - 1)
  {
    lcd.print(F("    FINISHED    "));
    return 0;
  }
  else
  {
    lcd.print(F("UNEXPECTED STOP "));
    return 1;
  }

  if (inputState == INFUSING)
    inputState = FINISHED;

}



/********************************************************************
  ISR(TIMER1_COMPA_vect)
********************************************************************/
ISR(TIMER1_COMPA_vect)
{
  digitalWrite(STEP_PIN, HIGH);
  if (ustepCounter == ustepCounterLimit) TIMSK1 &= ~( 1 << OCIE1A);
  if ( digitalRead(endStopPin) == LOW) TIMSK1 &= ~( 1 << OCIE1A);
  delayMicroseconds(10);
  digitalWrite(STEP_PIN, LOW);
  ustepCounter++;
}
