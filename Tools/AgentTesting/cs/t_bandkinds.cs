return string.Join(" | ", Protobot.Motion.ElasticKind.All.Select(k => k.key + "=" + k.shortName + " (" + k.source + ")"));
