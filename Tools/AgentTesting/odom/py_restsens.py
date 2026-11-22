{config}
# Standing still: the tracking sensors should not count (marks every 2 s for 20 s)
vert = Rotation(Ports.PORT{vert})
horiz = Rotation(Ports.PORT{horiz})
for i in range(11):
    print("mark rest", i, "vert", vert.position(DEGREES), "horiz", horiz.position(DEGREES))
    wait(2000, MSEC)
