using Rage;
using Rage.Native;
using RAGENativeUI;
using RAGENativeUI.Elements;
using System.Reflection;

[assembly: Rage.Attributes.Plugin("DeathCam", Description = "Removes the filter and fade to black when the player dies, and allows the camera to move freely.", Author = "SSStuart", PrefersSingleInstance = true, SupportUrl = "https://ssstuart.net/discord")]

namespace DeathCam
{
    public static class EntryPoint
    {
        public static string pluginName = "DeathCam";
        public static string pluginVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();

        private static Camera deathCamera;
        private static BigMessageHandler bigMessage;

        public static void Main()
        {
            Game.LogTrivial($"{pluginName} Plugin v{pluginVersion} has been loaded.");

            UpdateChecker.CheckForUpdates();

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
                    bool shouldRespawnInPlace = false;

                    EnableCamera();

                    while (!revived)
                    {
                        GameFiber.Yield();
                        // Reset fade out
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
                        {
                            revived = true;
                        } else if (Game.IsControlPressed(2, GameControl.Cover))
                        {
                            revived = true;
                            shouldRespawnInPlace = true;
                        }
                    }

                    Respawn(shouldRespawnInPlace);
                }
            });
        }

        private static void EnableCamera()
        {
            Game.LogTrivial($"[{pluginName}] Player has died, starting DeathCam sequence.");
            
            Game.LocalPlayer.IsIgnoredByEveryone = true;
            deathCamera = new Camera(false)
            {
                FOV = NativeFunction.Natives.GET_GAMEPLAY_CAM_FOV<float>(),
                Position = NativeFunction.Natives.GET_GAMEPLAY_CAM_COORD<Vector3>(),
                Rotation = NativeFunction.Natives.GET_GAMEPLAY_CAM_ROT<Rotator>()
            };
            deathCamera.PointAtEntity(Game.LocalPlayer.Character, new Vector3(), true);
            deathCamera.Shake("HAND_SHAKE", 0.01f);
            deathCamera.Active = true;
            Game.LogTrivial($"[{pluginName}] Camera enabled.");

            uint DeathTimeout = Game.GameTime + 3500;
            while (Game.GameTime < DeathTimeout)
            {
                GameFiber.Yield();
                // Resetting fade out and timescale
                if (Game.IsScreenFadingOut)
                    Game.FadeScreenIn(0);
                Game.TimeScale = 1.0f;
            }
            NativeFunction.Natives.ANIMPOSTFX_STOP_ALL();
            NativeFunction.Natives.STOP_CAM_POINTING(deathCamera);
            bigMessage.ShowColoredShard(" ", "Press ~b~" + GameControl.Jump + "~w~ to respawn", HudColor.Damage, HudColor.InGameBackground, 2000);
        }

        private static void Respawn(bool samePlace = false)
        {
            if (samePlace)
            {
                Game.LogTrivial($"[{pluginName}] Respawning is same place");
                Vector3 deathPosition = Game.LocalPlayer.Character.Position;
                Game.DisableAutomaticRespawn = true;
                Game.FadeScreenOutOnDeath = false;
                Game.LocalPlayer.Character.Health = Game.LocalPlayer.Character.MaxHealth;
                Game.LocalPlayer.Character.Resurrect();
                NativeFunction.Natives.SET_PED_TO_RAGDOLL(Game.LocalPlayer.Character, 1000, 5000, 2, false, false, false);
                Game.LocalPlayer.Character.Velocity = Vector3.Zero;
                Game.HandleRespawn();
            }
            else
            {
                Game.LogTrivial($"[{pluginName}] Letting the game handle the respawn (hospital)");
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

            Game.LogTrivial("Resetting respawn pos");
            NativeFunction.Natives.CLEAR_RESTART_COORD_OVERRIDE();
        }

    }
}
