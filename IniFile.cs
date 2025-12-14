using Rage;

namespace DeathCam
{
    internal static class Settings
    {
        internal static bool RESPAWN_IN_PLACE = false;
        internal static bool HIDE_WASTED_MESSAGE = false;
        internal static bool CAMERA_SHAKE = true;

        internal static void LoadSettings()
        {
            Game.LogTrivial("Loading plugin settings");
            var path = "Plugins/DeathCam.ini";
            var ini = new InitializationFile(path);
            ini.Create();

            RESPAWN_IN_PLACE = ini.ReadBoolean("General", "RespawnInPlace", false);
            Game.LogTrivial($"- Respawn in place: {(RESPAWN_IN_PLACE ? "Yes" : "No")}");
            HIDE_WASTED_MESSAGE = ini.ReadBoolean("General", "HideWastedMessage", false);
            Game.LogTrivial($"- Hide 'Wasted' message: {(HIDE_WASTED_MESSAGE ? "Yes" : "No")}");
            CAMERA_SHAKE = ini.ReadBoolean("Camera", "CameraShake", true);
            Game.LogTrivial($"- Camera shake: {(CAMERA_SHAKE? "Yes" : "No")}");

            Game.LogTrivial($"Plugin settings loaded.");
        }
    }
}
