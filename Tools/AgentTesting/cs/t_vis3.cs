// (Play mode, after t_vis1.cs) opens the Devices panel on the Vision Sensor's card (its picture and boxes).
var sensor = Protobot.Devices.V5Brain.Devices.OfType<Protobot.Devices.VisionSensor>().FirstOrDefault();
if (sensor == null) return "run t_vis1.cs first";
Protobot.Devices.DevicesPanel.Focus(sensor.part);
return "opened on port " + sensor.port + ", " + sensor.blobs.Count + " objects";
