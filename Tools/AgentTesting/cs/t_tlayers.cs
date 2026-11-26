var t = Protobot.Game.SkillsGame.Current.Toggles[0]; var c = t.GetComponentInChildren<Collider>();
return "ignore16-18 " + Physics.GetIgnoreLayerCollision(16, 18) + " barLayer " + c.gameObject.layer + " scene " + t.gameObject.scene.name + " enabled " + c.enabled + " bounds " + c.bounds.size + " mid " + t.transform.TransformPoint(t.pivotLocal) + " bcenter " + c.bounds.center;
