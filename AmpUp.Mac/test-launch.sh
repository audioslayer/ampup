#!/bin/bash
# AmpUp Test Launcher — launches dev build, watches for quit signal

pkill -f AmpUp.Mac 2>/dev/null
rm -f /tmp/ampup-quit
sleep 1

cd ~/Projects/AmpUp.Mac/AmpUp.Mac/bin/Debug/net8.0/osx-arm64
./AmpUp.Mac &
PID=$!

# Poll for quit signal every half second
while kill -0 $PID 2>/dev/null; do
    if [ -f /tmp/ampup-quit ]; then
        rm -f /tmp/ampup-quit
        kill -9 $PID 2>/dev/null
        exit 0
    fi
    sleep 0.5
done
