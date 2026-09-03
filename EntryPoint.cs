using Rage;
using Rage.Native;
using RAGENativeUI;
using RAGENativeUI.Elements;
using System.IO;
using System.Reflection;
using Localization = DeleteThatEntity.Localization;

[assembly: Rage.Attributes.Plugin("DeathCam", Description = "Removes the filter and fade to black when the player dies, and allows the camera to move freely.", Author = "SSStuart", PrefersSingleInstance = true, SupportUrl = "https://ssstuart.net/discord")]

namespace DeathCam
{
    public static class EntryPoint
    {
        public static string pluginName = "DeathCam";
        public static string pluginVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();
        public static Localization l10n = new Localization();

        private static Camera deathCamera;
        private static BigMessageHandler bigMessage;
        private static bool revived = true;

        public static void Main()
        {
            Game.LogTrivial($"{pluginName} plugin v{pluginVersion} has been loaded.");

            UpdateChecker.CheckForUpdates();

            if (IsMenyooManualRespawnEnabled())
            {
                do
                {
                    GameFiber.Yield();
                    GameFiber.Sleep(5000);
                } while (Game.IsLoading);
                Game.DisplayNotification("commonmenu", "mp_alerttriangle", pluginName, $"V {pluginVersion}", l10n.GetString("menyooConflict"));
                Game.LogTrivial("Menyoo \"Manual Respawn\" setting enabled. Stopping...");
                return;
            } else if (IsMenyooInstalled())
            {
                do
                {
                    GameFiber.Yield();
                    GameFiber.Sleep(5000);
                } while (Game.IsLoading);
                Game.DisplayNotification("commonmenu", "mp_alerttriangle", pluginName, $"V {pluginVersion}", l10n.GetString("menyooWarning"));
            }

            Settings.LoadSettings();

            float cameraSpeedFactor = 1;
            Game.DisableAutomaticRespawn = true;
            Game.FadeScreenOutOnDeath = false;
            BigMessageThread bigMessageThread = new BigMessageThread();
            bigMessage = bigMessageThread.MessageInstance;

            GameFiber.StartNew(delegate
            {
                while (true)
                {

                    while (Game.LocalPlayer.Character.IsAlive)
                    {
                        GameFiber.Yield();
                    }

                    // Player died
                    revived = false;
                    bool respawnInPlace = false;

                    EnableCamera();

                    while (!revived)
                    {
                        GameFiber.Yield();
                        // Reset fade out
                        NativeFunction.Natives.ANIMPOSTFX_STOP_ALL();
                        if (Game.IsScreenFadingOut)
                            Game.FadeScreenIn(0);

                        // Camera rotation
                        float yRotMagnitude = NativeFunction.CallByName<float>("GET_CONTROL_NORMAL", 0, (int)GameControl.LookUpDown) * (Game.IsControllerConnected ? 2.5f : 10);
                        float xRotMagnitude = NativeFunction.CallByName<float>("GET_CONTROL_NORMAL", 0, (int)GameControl.LookLeftRight) * (Game.IsControllerConnected ? 2.5f : 10);

                        float newPitch = deathCamera.Rotation.Pitch - yRotMagnitude;
                        float newYaw = deathCamera.Rotation.Yaw - xRotMagnitude;
                        deathCamera.Rotation = new Rotator((newPitch >= 89.5f || newPitch <= -89.5f) ? deathCamera.Rotation.Pitch : newPitch, 0f, newYaw);

                        // Camera zoom (scoll wheel)
                        if (Game.GetMouseWheelDelta() < 0)
                            deathCamera.FOV += 2;
                        else if (Game.GetMouseWheelDelta() > 0)
                            deathCamera.FOV -= 2;
                        // Camera zomm (with controller)
                        if (Game.IsControlPressed(2, GameControl.CellphoneDown))
                            deathCamera.FOV += 0.5f;
                        else if (Game.IsControlPressed(2, GameControl.CellphoneUp))
                            deathCamera.FOV -= 0.5f;

                        // Camera movement speed
                        //  Increase  (Shift for keyboard | Attack for controller)
                        if (Game.IsShiftKeyDownRightNow || Game.IsControlPressed(2, GameControl.Attack))
                            cameraSpeedFactor += 0.1f;
                        //  Decrease  (Ctrl for keyboard | Aim for controller)
                        else if (Game.IsControlKeyDownRightNow|| Game.IsControlPressed(2, GameControl.Aim))
                            cameraSpeedFactor = MathHelper.Max(1f, cameraSpeedFactor - 0.1f);

                        // Camera movements
                        if (Game.IsControlPressed(2, GameControl.MoveUpOnly) && deathCamera.DistanceTo(Game.LocalPlayer.Character) > 1)
                            deathCamera.Position += deathCamera.ForwardVector * 0.1f * cameraSpeedFactor;
                        else if (Game.IsControlPressed(2, GameControl.MoveDownOnly))
                            deathCamera.Position -= deathCamera.ForwardVector * 0.1f * cameraSpeedFactor;
                        if (Game.IsControlPressed(2, GameControl.MoveRightOnly))
                            deathCamera.Position += deathCamera.RightVector * 0.1f * cameraSpeedFactor;
                        else if (Game.IsControlPressed(2, GameControl.MoveLeftOnly))
                            deathCamera.Position -= deathCamera.RightVector * 0.1f * cameraSpeedFactor;

                        // Respawn condition
                        if (Game.LocalPlayer.Character.Health > Game.LocalPlayer.Character.FatalInjuryHealthThreshold
                            || Game.IsControlPressed(2, GameControl.Jump))
                        {
                            revived = true;
                            respawnInPlace = Settings.RESPAWN_IN_PLACE == "yes" || (Game.IsControlKeyDownRightNow && Settings.RESPAWN_IN_PLACE == "choice");
                        }
                    }

                    Respawn(respawnInPlace);
                }
            });
        }

        private static void EnableCamera()
        {
            Game.LogTrivial($"Player has died, starting DeathCam sequence.");

            Game.LocalPlayer.IsIgnoredByEveryone = true;
            deathCamera = new Camera(false)
            {
                FOV = NativeFunction.Natives.GET_GAMEPLAY_CAM_FOV<float>(),
                Position = NativeFunction.Natives.GET_GAMEPLAY_CAM_COORD<Vector3>(),
                Rotation = NativeFunction.Natives.GET_GAMEPLAY_CAM_ROT<Rotator>()
            };
            deathCamera.PointAtEntity(Game.LocalPlayer.Character, new Vector3(), true);
            if (Settings.CAMERA_SHAKE)
               deathCamera.Shake("HAND_SHAKE", 0.01f);
            deathCamera.Active = true;
            Game.LogTrivial($"Camera enabled.");

            uint DeathTimeout = Game.GameTime + 3500;
            while (Game.GameTime < DeathTimeout)
            {
                GameFiber.Yield();
                // Resetting fade out and timescale
                if (Game.IsScreenFadingOut)
                    Game.FadeScreenIn(0);
                Game.TimeScale = 1.0f;
                NativeFunction.Natives.ANIMPOSTFX_STOP_ALL();
                if (Settings.HIDE_WASTED_MESSAGE)
                    bigMessage.ShowOldMessage("", 0);
            }
            NativeFunction.Natives.STOP_CAM_POINTING(deathCamera);
            if (!Settings.HIDE_WASTED_MESSAGE)
                bigMessage.ShowColoredShard(l10n.GetString("wasted"), l10n.GetString(Settings.RESPAWN_IN_PLACE == "choice" ? "pressJumpToRespawnChoice" : "pressJumpToRespawn", ("jumpControl", GameControl.Jump)), HudColor.Red, HudColor.InGameBackground, 2000);
            Game.LocalPlayer.WantedLevel = 0;
            GameFiber.Wait(2000);
            foreach (Ped ped in World.GetAllPeds())
            {
                if (ped.CombatTarget == Game.LocalPlayer.Character)
                {
                    if (ped.IsOnFoot)
                        ped.Tasks.AimWeaponAt(Game.LocalPlayer.Character, MathHelper.GetRandomInteger(5) * 1000);
                    else
                        ped.Tasks.Clear();
                }
               
            }
        }

        private static void Respawn(bool respawnInPlace)
        {
            if (respawnInPlace)
            {
                Game.LogTrivial($"Respawning is same place");
                Vector3 respawnPos = Game.LocalPlayer.Character.Position;
                Game.DisableAutomaticRespawn = true;
                Game.FadeScreenOutOnDeath = false;

                // Inspired by https://github.com/gta-chaos-mod/ChaosModV/blob/9301f701dcd29e558a7955f260a3fcb327e66a21/ChaosMod/Components/CrossingChallenge.cpp#L33C1-L33C41
                Game.TerminateAllScriptsWithName("respawn_controller");
                Game.FadeScreenOut(200);
                NativeFunction.Natives.NETWORK_REQUEST_CONTROL_OF_ENTITY<bool>(Game.LocalPlayer.Character);
                NativeFunction.Natives.NETWORK_RESURRECT_LOCAL_PLAYER(respawnPos.X, respawnPos.Y, respawnPos.Z, Game.LocalPlayer.Character.Heading, false, false, false, 0, 0);
                GameFiber.Sleep(2000);
                NativeFunction.Natives.FORCE_GAME_STATE_PLAYING();
                NativeFunction.Natives.RESET_PLAYER_ARREST_STATE(Game.LocalPlayer.Character);
                NativeFunction.Natives.DISPLAY_HUD(true);
                Game.FadeScreenIn(200);
            }
            else
            {
                Game.LogTrivial($"Letting the game handle the respawn (hospital)");
                Game.HandleRespawn();
                Game.FadeScreenOut(500); GameFiber.Sleep(500);
            }

            Game.LogTrivial("Resetting player attribute and removing cam");
            Game.LocalPlayer.IsIgnoredByEveryone = false;
            deathCamera.Active = false;
            if (deathCamera.Exists())
                deathCamera.Delete();
            deathCamera = null;

            do
            {
                GameFiber.Sleep(5000);
            } while (Game.IsScreenFadedOut);
        }

        private static bool IsMenyooInstalled()
        {
            return File.Exists("./Menyoo.asi");
        }

        private static bool IsMenyooManualRespawnEnabled()
        {
            if (File.Exists("./menyooStuff/menyooConfig.ini"))
            {
                string[] lines = File.ReadAllLines("./menyooStuff/menyooConfig.ini");
                foreach (string line in lines)
                {
                    if (line.Contains("manual_respawn") && (line.Contains("= true") || line.Contains("= 1")))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static void OnUnload(bool variable)
        {
            Game.LogTrivial("Unloading...");
            if (!revived)
                Respawn(false);
            Game.LogTrivial("Unloaded");
        }
    }
}
