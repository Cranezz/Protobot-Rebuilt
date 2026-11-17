from vex import *
L = MotorGroup(Motor(Ports.PORT4, GearSetting.RATIO_6_1, False), Motor(Ports.PORT5, GearSetting.RATIO_6_1, False), Motor(Ports.PORT6, GearSetting.RATIO_6_1, False))
R = MotorGroup(Motor(Ports.PORT1, GearSetting.RATIO_6_1, True), Motor(Ports.PORT2, GearSetting.RATIO_6_1, True), Motor(Ports.PORT3, GearSetting.RATIO_6_1, True))
def autonomous():
    print("auton")
    L.spin(FORWARD, 4, VOLT)
    R.spin(FORWARD, 4, VOLT)
    wait(0.8, SECONDS)
    L.stop(BRAKE)
    R.stop(BRAKE)
def driver():
    print("driver")
    L.spin(FORWARD, 3, VOLT)
    R.spin(REVERSE, 3, VOLT)
    wait(1, SECONDS)
    L.stop(BRAKE)
    R.stop(BRAKE)
comp = Competition(driver, autonomous)
