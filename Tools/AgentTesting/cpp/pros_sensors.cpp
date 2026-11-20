// GPS and AI Vision in PROS (compile check for the VS Code headers; also runs in the simulator)
#include "main.h"

pros::Gps gps(8, 0.0, -0.2);
pros::AIVision ai(9);

void initialize() {
  ai.set_color(pros::AIVision::Color{1, 200, 30, 30, 10, 0.2});
  ai.enable_detection_types(pros::AivisionModeType::colors | pros::AivisionModeType::objects);
}

void opcontrol() {
  while (true) {
    pros::gps_status_s_t s = gps.get_status();
    double h = gps.get_heading();
    int n = ai.get_object_count();
    for (int i = 0; i < n; i++) {
      pros::AIVision::Object o = ai.get_object(i);
      if (pros::AIVision::is_type(o, pros::AivisionDetectType::object))
        printf("piece %d score %d at %d\n", o.id, o.object.element.score, o.object.element.xoffset);
      else if (pros::AIVision::is_type(o, pros::AivisionDetectType::color))
        printf("color %d w %d\n", o.id, o.object.color.width);
    }
    printf("gps %.2f %.2f %.1f err %.3f\n", s.x, s.y, h, gps.get_error());
    pros::delay(50);
  }
}
