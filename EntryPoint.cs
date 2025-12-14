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
                    bool revived = false;

                    EnableCamera();

                    while (!revived)
                    {
                        GameFiber.Yield();
                        // Reset fade out
                        NativeFunction.Natives.ANIMPOSTFX_STOP_ALL();
                        if (Game.IsScreenFadingOut)
                            Game.FadeScreenIn(0);

                        // Camera rotation
                        float yRotMagnitude = NativeFunction.CallByName<float>("GET_CONTROL_NORMAL", 0, (int)GameControl.LookUpDown) * 10f;
                        float xRotMagnitude = NativeFunction.CallByName<float>("GET_CONTROL_NORMAL", 0, (int)GameControl.LookLeftRight) * 10f;

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

                        // Controller support for increasing/decreasing camera movement speed (Sprint control doesn't work)
                        if (Game.IsControlPressed(2, GameControl.Duck))
                            cameraSpeedFactor += 0.1f;
                        else
                            cameraSpeedFactor = MathHelper.Max(1f, cameraSpeedFactor - 0.2f);

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
                            revived = true;
                    }

                    Respawn();
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
                bigMessage.ShowColoredShard(l10n.GetString("wasted"), l10n.GetString("pressJumpToRespawn", ("jumpControl", GameControl.Jump)), HudColor.Red, HudColor.InGameBackground, 2000);
            Game.LocalPlayer.WantedLevel = 0;
        }

        private static void Respawn()
        {
            //if (Settings.RESPAWN_IN_PLACE)
            //{
            //    Game.LogTrivial($"Respawning is same place");
            //    //ToggleHospitals(false);
            //    Game.DisableAutomaticRespawn = true;
            //    Game.FadeScreenOutOnDeath = false;
            //    Game.LocalPlayer.Character.Resurrect();
            //    GameFiber.Sleep(10);
            //    Game.LocalPlayer.Character.Velocity = Vector3.Zero;
            //    Game.HandleRespawn();
            //}
            //else
            //{
                Game.LogTrivial($"Letting the game handle the respawn (hospital)");
                Game.HandleRespawn();
                Game.FadeScreenOut(500); GameFiber.Sleep(500);
            //}

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

            //Game.LogTrivial("Resetting respawn pos");
            //ToggleHospitals(true);
        }

        private static void ToggleHospitals(bool enable)
        {
            Game.LogTrivial((enable ? "Enabling" : "Disabling") + " all hospitals");
            for (int hospital = 0; hospital < 5; hospital++)
                NativeFunction.Natives.DISABLE_HOSPITAL_RESTART(hospital, !enable);

            if (enable)
            {
                NativeFunction.Natives.CLEAR_RESTART_COORD_OVERRIDE();
            }
            else
            {
                NativeFunction.Natives.SET_RESTART_COORD_OVERRIDE(Game.LocalPlayer.Character.Position.X, Game.LocalPlayer.Character.Position.Y, Game.LocalPlayer.Character.Position.Z, Game.LocalPlayer.Character.Heading);
            }
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
    }
}
