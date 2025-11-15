using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;

namespace ToasterCameras;

public static class KeybindDumper
{
    public static void DumpAllKeybinds()
    {
        var output = new StringBuilder();
        
        output.AppendLine("=== AVAILABLE KEYBINDS ===");
        output.AppendLine("Note: Use these paths in your config JSON\n");
        
        // Get all registered input device layouts
        foreach (var layout in InputSystem.ListLayouts())
        {
            if (layout.Contains("Keyboard"))
            {
                DumpKeyboard(output);
            }
            else if (layout.Contains("Mouse"))
            {
                DumpMouse(output);
            }
            else if (layout.Contains("Gamepad"))
            {
                DumpGamepad(output);
            }
        }
        
        // Dump actually connected devices
        output.AppendLine("\n=== CURRENTLY CONNECTED DEVICES ===");
        if (InputSystem.devices.Count == 0)
        {
            output.AppendLine("No devices detected yet (may populate after game starts)");
        }
        
        foreach (var device in InputSystem.devices)
        {
            output.AppendLine($"\n{device.displayName} ({device.layout}):");
            
            foreach (var control in device.allControls)
            {
                // Only show button-like controls
                if (control is UnityEngine.InputSystem.Controls.ButtonControl ||
                    control.name.Contains("button") ||
                    control.name.Contains("key") ||
                    control.name.Contains("trigger"))
                {
                    output.AppendLine($"  {control.path}");
                }
            }
        }
        
        string path = Path.Combine(
            Path.GetDirectoryName(ModSettings.GetConfigPath()), 
            "ToasterCameras_available_keybinds.txt"
        );
        File.WriteAllText(path, output.ToString());
        
        Plugin.Log($"Dumped keybinds to: {path}");
    }
    
    private static void DumpKeyboard(StringBuilder output)
    {
        output.AppendLine("=== KEYBOARD ===");
        output.AppendLine("Format: <Keyboard>/key");
        output.AppendLine("Examples:");
        output.AppendLine("  <Keyboard>/a through <Keyboard>/z");
        output.AppendLine("  <Keyboard>/1 through <Keyboard>/0");
        output.AppendLine("  <Keyboard>/f1 through <Keyboard>/f12");
        output.AppendLine("  <Keyboard>/numpad0 through <Keyboard>/numpad9");
        output.AppendLine("  <Keyboard>/space");
        output.AppendLine("  <Keyboard>/enter");
        output.AppendLine("  <Keyboard>/escape");
        output.AppendLine("  <Keyboard>/leftShift, <Keyboard>/rightShift");
        output.AppendLine("  <Keyboard>/leftCtrl, <Keyboard>/rightCtrl");
        output.AppendLine("  <Keyboard>/leftAlt, <Keyboard>/rightAlt");
        output.AppendLine("  <Keyboard>/tab");
        output.AppendLine("  <Keyboard>/backspace");
        output.AppendLine("  <Keyboard>/upArrow, <Keyboard>/downArrow, <Keyboard>/leftArrow, <Keyboard>/rightArrow");
        output.AppendLine("  <Keyboard>/insert, <Keyboard>/delete, <Keyboard>/home, <Keyboard>/end");
        output.AppendLine("  <Keyboard>/pageUp, <Keyboard>/pageDown");
        output.AppendLine();
    }
    
    private static void DumpMouse(StringBuilder output)
    {
        output.AppendLine("=== MOUSE ===");
        output.AppendLine("  <Mouse>/leftButton");
        output.AppendLine("  <Mouse>/rightButton");
        output.AppendLine("  <Mouse>/middleButton");
        output.AppendLine("  <Mouse>/forwardButton");
        output.AppendLine("  <Mouse>/backButton");
        output.AppendLine();
    }
    
    private static void DumpGamepad(StringBuilder output)
    {
        output.AppendLine("=== GAMEPAD ===");
        output.AppendLine("  <Gamepad>/buttonSouth (A on Xbox, Cross on PS)");
        output.AppendLine("  <Gamepad>/buttonEast (B on Xbox, Circle on PS)");
        output.AppendLine("  <Gamepad>/buttonWest (X on Xbox, Square on PS)");
        output.AppendLine("  <Gamepad>/buttonNorth (Y on Xbox, Triangle on PS)");
        output.AppendLine("  <Gamepad>/leftShoulder (LB/L1)");
        output.AppendLine("  <Gamepad>/rightShoulder (RB/R1)");
        output.AppendLine("  <Gamepad>/leftTrigger (LT/L2)");
        output.AppendLine("  <Gamepad>/rightTrigger (RT/R2)");
        output.AppendLine("  <Gamepad>/leftStickPress (L3)");
        output.AppendLine("  <Gamepad>/rightStickPress (R3)");
        output.AppendLine("  <Gamepad>/start");
        output.AppendLine("  <Gamepad>/select");
        output.AppendLine("  <Gamepad>/dpad/up");
        output.AppendLine("  <Gamepad>/dpad/down");
        output.AppendLine("  <Gamepad>/dpad/left");
        output.AppendLine("  <Gamepad>/dpad/right");
        output.AppendLine();
    }
}