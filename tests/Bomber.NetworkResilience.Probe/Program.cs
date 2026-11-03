var origin = args.First(value => !value.StartsWith("--", StringComparison.Ordinal));
await NetworkBlackoutChecks.Run(new Uri(origin), args);
