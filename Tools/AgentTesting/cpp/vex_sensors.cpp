// GPS and AI Vision in VEXcode C++ (compile check for the VS Code headers; also runs in the simulator)
#include "vex.h"
using namespace vex;

brain Brain;
gps GPS8 = gps(PORT8, 0.00, -216.92, mm, 180, right);
aivision::colordesc AIVision9__RED = aivision::colordesc(1, 200, 30, 30, 10, 0.2);
aivision AIVision9 = aivision(PORT9, AIVision9__RED, aivision::ALL_AIOBJS);

int main() {
  while (true) {
    Brain.Screen.clearScreen();
    Brain.Screen.setCursor(1, 1);
    Brain.Screen.print("x %.1f y %.1f h %.0f q %d", GPS8.xPosition(inches), GPS8.yPosition(inches), GPS8.heading(), (int)GPS8.quality());
    int n = AIVision9.takeSnapshot(aivision::ALL_AIOBJS);
    if (n > 0 && AIVision9.objects[0].exists) {
      Brain.Screen.newLine();
      Brain.Screen.print("piece %d score %d", AIVision9.objects[0].id, AIVision9.objects[0].score);
    }
    AIVision9.takeSnapshot(AIVision9__RED);
    wait(50, msec);
  }
}
