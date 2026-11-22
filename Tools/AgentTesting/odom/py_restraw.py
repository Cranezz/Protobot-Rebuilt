{config}
# Tracking wheels at rest: the raw Rotation positions every 2 s, first standing still from the start, then after a
# drive and a stop (they should not count up; run.sh prints the truth beside each mark)
vert = Rotation(Ports.PORT{vert}, True)
horiz = Rotation(Ports.PORT{horiz}, True)


def mark(name):
    print("mark", name, "vert=%.3f horiz=%.3f" % (vert.position(DEGREES), horiz.position(DEGREES)))


for i in range(5):
    mark("rest%d" % i)
    wait(2000, MSEC)
left_drive.spin(FORWARD, 60, PERCENT)
right_drive.spin(FORWARD, 60, PERCENT)
wait(1000, MSEC)
left_drive.stop(BRAKE)
right_drive.stop(BRAKE)
wait(1000, MSEC)
for i in range(8):
    mark("after%d" % i)
    wait(2000, MSEC)
