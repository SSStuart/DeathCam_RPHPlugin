using Rage;
using Rage.Native;
using RAGENativeUI;
using RAGENativeUI.Elements;

[assembly: Rage.Attributes.Plugin("DeathCam", Description = "Removes the filter and fade to black when the player dies, and allows the camera to move freely.", Author = "SSStuart")]


namespace DeathCam
{
    public static class EntryPoint
    {
        public static string pluginName = "DeathCam";
        public static string pluginVersion = "v 0.0.3";
        public static void Main()
        {
            Game.LogTrivial(pluginName + " loaded.");
            
            Camera deathCamera;
            float cameraSpeedFactor = 1;
            Game.DisableAutomaticRespawn = true;
            Game.FadeScreenOutOnDeath = false;

            GameFiber.StartNew(delegate
            {

                while (true)
                {
                    GameFiber.Yield();
                    
                    if (!Game.LocalPlayer.Character.IsAlive)
                    {
                        bool revived = false;

                        Game.LocalPlayer.IsIgnoredByEveryone = true;
                        deathCamera = new Camera(false)
                        {
                            FOV = NativeFunction.Natives.GET_GAMEPLAY_CAM_FOV<float>(),
                            Position = NativeFunction.Natives.GET_GAMEPLAY_CAM_COORD<Vector3>(),
                            Rotation = NativeFunction.Natives.GET_GAMEPLAY_CAM_ROT<Rotator>()
                        };
                        deathCamera.PointAtEntity(Game.LocalPlayer.Character, new Vector3(), true);
                        deathCamera.Shake("HAND_SHAKE", 0.03f);
                        deathCamera.Active = true;

                        BigMessageThread bigMessageThread = new BigMessageThread(true);
                        BigMessageHandler bigMessage = bigMessageThread.MessageInstance;
                        uint DeathTimeout = Game.GameTime + 3000;

                        while (Game.GameTime < DeathTimeout)
                        {
                            GameFiber.Yield();
                            if (Game.IsScreenFadingOut)
                                Game.FadeScreenIn(0);
                            Game.TimeScale = 1.0f;
                        }
                        NativeFunction.Natives.ANIMPOSTFX_STOP_ALL();
                        bigMessage.ShowColoredShard(" ", "Press ~b~"+GameControl.Jump+"~w~ to respawn", HudColor.Damage, HudColor.InGameBackground, 2000);

                        GameFiber.Sleep(500);
                        NativeFunction.Natives.STOP_CAM_POINTING(deathCamera);

                        while (!Game.IsControlPressed(2, GameControl.Jump) && !revived)
                        {
                            GameFiber.Yield();
                            if (Game.IsScreenFadingOut)
                                Game.FadeScreenIn(0);

                            float yRotMagnitude = NativeFunction.CallByName<float>("GET_CONTROL_NORMAL", 0, (int)GameControl.LookUpDown) * 10f;
                            float xRotMagnitude = NativeFunction.CallByName<float>("GET_CONTROL_NORMAL", 0, (int)GameControl.LookLeftRight) * 10f;

                            float newPitch = deathCamera.Rotation.Pitch - yRotMagnitude;
                            float newYaw = deathCamera.Rotation.Yaw - xRotMagnitude;
                            deathCamera.Rotation = new Rotator((newPitch >= 89.5f || newPitch <= -89.5f) ? deathCamera.Rotation.Pitch : newPitch, 0f, newYaw);

                            if (Game.GetMouseWheelDelta() < 0)
                                deathCamera.FOV += 2;
                            else if (Game.GetMouseWheelDelta() > 0)
                                deathCamera.FOV -= 2;

                            if (Game.IsShiftKeyDownRightNow)
                                cameraSpeedFactor += 0.1f;
                            else
                                cameraSpeedFactor = MathHelper.Max(1f, cameraSpeedFactor - 0.2f);
                            if (Game.IsControlPressed(2, GameControl.MoveUpOnly) && deathCamera.DistanceTo(Game.LocalPlayer.Character) > 1)
                                deathCamera.Position += deathCamera.ForwardVector * 0.1f * cameraSpeedFactor;
                            else if (Game.IsControlPressed(2, GameControl.MoveDownOnly))
                                deathCamera.Position -= deathCamera.ForwardVector * 0.1f * cameraSpeedFactor;
                            if (Game.IsControlPressed(2, GameControl.MoveRightOnly))
                                deathCamera.Position += deathCamera.RightVector * 0.1f * cameraSpeedFactor;
                            else if (Game.IsControlPressed(2, GameControl.MoveLeftOnly))
                                deathCamera.Position -= deathCamera.RightVector * 0.1f * cameraSpeedFactor;
                            
                            if (Game.LocalPlayer.Character.Health > Game.LocalPlayer.Character.FatalInjuryHealthThreshold)
                            {
                                GameFiber.Sleep(1000);
                                revived = true;
                            }
                        }
                        Game.LocalPlayer.IsIgnoredByEveryone = false;
                        Game.FadeScreenOut(500); GameFiber.Sleep(500);
                        Game.HandleRespawn();
                        deathCamera.Active = false;
                        deathCamera = null;

                        GameFiber.Sleep(10000);
                    }
                }
            });
        }
    }
}
