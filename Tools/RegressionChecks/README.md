Run the hardware-independent release checks from the repository root:

```powershell
dotnet run --project Tools/RegressionChecks/RegressionChecks.csproj -c Release
```

The checks render real RGB frames, exercise mute-notification routing without starting the app, and feed synthetic audio into the FFT. They open no serial, HID, WASAPI, or VoiceMeeter connection. Failures produce a nonzero exit code. Reflection keeps these checks separate from the production startup path.

They cover both mute effects, app-group reset, master/mic/VM notification routing, polling cadence, app-name matching, FFT sensitivity, overlapping audio windows, and spectrum boundaries. They do not replace live device testing.
