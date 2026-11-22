{config}
# Drives back and forth slowly for the lean check (odom/lean.sh)
for i in range(6):
    left_drive.spin(FORWARD if i % 2 == 0 else REVERSE, 40, PERCENT)
    right_drive.spin(FORWARD if i % 2 == 0 else REVERSE, 40, PERCENT)
    wait(1500, MSEC)
    left_drive.stop(BRAKE)
    right_drive.stop(BRAKE)
    wait(1500, MSEC)
print("mark", "END")
