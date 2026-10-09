using System.Text.Json;
using RecompLauncher.Models;

namespace RecompLauncher;

/// <summary>
/// The standard ReXGlue settings tabs written into newly created profiles.
/// These cvars are provided by the ReXGlue runtime itself, so the same set
/// works for every recomp built against it.
/// </summary>
public static class DefaultTabs
{
    public const string Json = """
    [
      {
        "name": "Display",
        "settings": [
          { "cvar": "fullscreen", "label": "Fullscreen", "type": "bool", "default": "false",
            "hint": "Start in fullscreen instead of a window." },
          { "cvar": "fullscreen_exclusive", "label": "Exclusive fullscreen", "type": "bool", "default": "false",
            "hint": "True exclusive mode instead of borderless desktop fullscreen. Change before launching.",
            "advanced": true },
          { "cvar": "monitor", "label": "Monitor", "type": "choice", "default": "0",
            "choices": [
              { "value": "0", "label": "Primary (0)" },
              { "value": "1", "label": "Monitor 1" },
              { "value": "2", "label": "Monitor 2" },
              { "value": "3", "label": "Monitor 3" }
            ],
            "hint": "Which display to use for fullscreen." },
          { "cvar": "resolution", "label": "Resolution", "type": "resolution", "default": "1280x720",
            "presets": [ "640x480", "800x600", "1024x768", "1280x720", "1600x900", "1920x1080", "2560x1440", "3840x2160" ],
            "hint": "Guest video mode and window size." },
          { "cvar": "vsync", "label": "V-Sync", "type": "bool", "default": "true",
            "hint": "Cap presentation to the display refresh rate." },
          { "cvar": "present_letterbox", "label": "Letterbox non-native aspect", "type": "bool", "default": "true",
            "advanced": true, "hint": "Draw black bars when the window aspect differs from the guest mode." },
          { "cvar": "video_mode_refresh_rate", "label": "Guest refresh rate", "type": "double",
            "default": "60", "min": 24, "max": 240, "advanced": true,
            "hint": "Refresh rate reported to the guest." }
        ]
      },
      {
        "name": "Graphics",
        "settings": [
          { "cvar": "resolution_scale", "label": "Internal resolution scale", "type": "int",
            "default": "1", "min": 1, "max": 8,
            "hint": "Render the guest framebuffer at Nx resolution (supersampling). 1 = native." },
          { "cvar": "anisotropic_override", "label": "Anisotropic filtering", "type": "choice", "default": "0",
            "choices": [
              { "value": "0", "label": "Game default (off)" },
              { "value": "2", "label": "2x" },
              { "value": "4", "label": "4x" },
              { "value": "8", "label": "8x" },
              { "value": "16", "label": "16x" }
            ],
            "hint": "Force anisotropic texture filtering quality." },
          { "cvar": "present_effect", "label": "Post-process effect", "type": "choice", "default": "none",
            "choices": [
              { "value": "none", "label": "None" },
              { "value": "cas", "label": "CAS (sharpness)" }
            ],
            "hint": "Presentation filter. 'cas' sharpens the image; tune with the CAS sharpness setting below." },
          { "cvar": "present_cas_additional_sharpness", "label": "CAS sharpness", "type": "double",
            "default": "0.2", "min": 0.0, "max": 1.0, "advanced": true,
            "hint": "Additional sharpness for the CAS effect (0-1)." },
          { "cvar": "present_dither", "label": "Dithering", "type": "bool", "default": "true",
            "advanced": true, "hint": "Apply dither on presentation." },
          { "cvar": "async_shader_compilation", "label": "Async shader compilation", "type": "bool",
            "default": "true", "hint": "Compile shaders on worker threads instead of hitching the game." },
          { "cvar": "video_driver", "label": "Video driver", "type": "choice", "default": "auto",
            "choices": [
              { "value": "auto", "label": "Auto" },
              { "value": "d3d12", "label": "Direct3D 12" },
              { "value": "vulkan", "label": "Vulkan" }
            ],
            "advanced": true, "hint": "Host graphics backend. Change before launching." },
          { "cvar": "d3d12_allow_variable_refresh_rate_and_tearing", "label": "Allow VRR / tearing (D3D12)",
            "type": "bool", "default": "false", "advanced": true,
            "hint": "Enable variable refresh rate present when supported (G-SYNC/FreeSync)." }
        ]
      },
      {
        "name": "Audio & Input",
        "settings": [
          { "cvar": "audio_mute", "label": "Mute audio", "type": "bool", "default": "false" },
          { "cvar": "vibration", "label": "Gamepad vibration", "type": "bool", "default": "true" },
          { "cvar": "left_stick_deadzone_percentage", "label": "Left stick deadzone", "type": "double",
            "default": "0.15", "min": 0.0, "max": 0.5, "hint": "0.15 = 15% radius ignored." },
          { "cvar": "right_stick_deadzone_percentage", "label": "Right stick deadzone", "type": "double",
            "default": "0.15", "min": 0.0, "max": 0.5, "hint": "0.15 = 15% radius ignored." },
          { "cvar": "mnk_mode", "label": "Mouse & keyboard mode", "type": "bool", "default": "false",
            "advanced": true, "hint": "Map keyboard/mouse to the guest controller." },
          { "cvar": "mnk_sensitivity", "label": "M&K sensitivity", "type": "double",
            "default": "1.0", "min": 0.1, "max": 10.0, "advanced": true },
          { "cvar": "guide_button", "label": "Guide button", "type": "bool", "default": "true",
            "advanced": true, "hint": "Pass the controller guide/home button to the guest." }
        ]
      },
      {
        "name": "System",
        "settings": [
          { "cvar": "user_language", "label": "Game language", "type": "choice", "default": "0",
            "choices": [
              { "value": "0", "label": "English" },
              { "value": "1", "label": "Japanese" },
              { "value": "2", "label": "German" },
              { "value": "3", "label": "French" },
              { "value": "4", "label": "Spanish" },
              { "value": "5", "label": "Italian" },
              { "value": "6", "label": "Korean" },
              { "value": "7", "label": "Chinese (Traditional)" },
              { "value": "8", "label": "Portuguese" },
              { "value": "9", "label": "Chinese (Simplified)" }
            ],
            "hint": "Language id reported to the game. The game only honors languages it ships." },
          { "cvar": "log_level", "label": "Log level", "type": "choice", "default": "info",
            "choices": [
              { "value": "trace", "label": "Trace" },
              { "value": "debug", "label": "Debug" },
              { "value": "info", "label": "Info" },
              { "value": "warning", "label": "Warning" },
              { "value": "error", "label": "Error" }
            ],
            "hint": "Runtime log verbosity; logs are written next to the game exe in logs\\." },
          { "cvar": "log_verbose", "label": "Verbose logging", "type": "bool", "default": "false", "advanced": true },
          { "cvar": "user_data_root", "label": "User data folder", "type": "text", "default": "",
            "advanced": true,
            "hint": "Where saves/cache live. Empty = Documents\\<game>. Restart the game after changing." },
          { "cvar": "user_country", "label": "Country", "type": "int", "default": "0", "advanced": true,
            "hint": "Xbox country id reported to the game (1 = USA, 11 = UK, ...)." }
        ]
      }
    ]
    """;

    public static List<SettingsTab> Tabs =>
        JsonSerializer.Deserialize<List<SettingsTab>>(Json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        }) ?? new List<SettingsTab>();
}
