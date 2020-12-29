using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.ModAPI.Ingame;
using VRage.Game;
using VRageMath;

namespace IngameScript {
    internal class Program : MyGridProgram {
        // Tag for controller block (remote control or cockpit)
        private const string CONTROLLER_TAG = "[CONTROLLER]";

        // Tag for projector
        private const string PROJECTOR_TAG = "[PROJECTOR]";

        // Tag for display, that will show remaining parts to build.
        private const string DISPLAY_REMAINING_TAG = "[DISPLAY:REMAINING]";

        // Tag for display, that will show projectors offset and rotation.
        private const string DISPLAY_OFFSET_TAG = "[DISPLAY:OFFSET";

        // Prefix before display index in 'display' block
        private const string SURFACE_INDEX_PREFIX = "PROJECTOR:";

        // End of configuration
        ////////////////////////////////////////////////////////////////////////////////

        private bool _InputHandled;
        private bool _SwapAxis;
        private IMyTextSurface _Surface;

        public Program() {
            Runtime.UpdateFrequency = UpdateFrequency.Update1;
            _Surface = Me.GetSurface(0);
        }

        public void Main(string argument, UpdateType updateSource) {
            Log("IslPrinterProjectorControl v1.0\n", true);
            if (argument == "swap") {
                Log("Swapping X anz Z axis ...");
                _SwapAxis = !_SwapAxis;
            }

            List<IMyShipController> controllers = new List<IMyShipController>();
            GridTerminalSystem.GetBlocksOfType(controllers, o => o.CustomName.Contains(CONTROLLER_TAG));

            List<IMyProjector> projectors = new List<IMyProjector>();
            GridTerminalSystem.GetBlocksOfType(projectors, o => o.CustomName.Contains(PROJECTOR_TAG));

            bool valid = true;

            if (controllers.Count == 0) {
                Log("Error: Cannot find controller.");
                valid = false;
            } else if (controllers.Count > 1) {
                Log("Warning: Found more than one controller. First one ('" + controllers[0].CustomName + "') will be used.");
            } else {
                Log("Found controller '" + controllers[0].CustomName + "'");
            }

            IMyShipController controller = controllers[0];

            if (projectors.Count == 0) {
                Log("Error: Cannot find projector.");
                valid = false;
            } else if (projectors.Count > 1) {
                Log("Warning: Found more than one projector. First one ('" + projectors[0].CustomName + "') will be used.");
            } else {
                Log("Found projector '" + projectors[0].CustomName + "'");
            }

            IMyProjector projector = projectors[0];

            List<IMyTerminalBlock> displays = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocksOfType(displays, o => o is IMyTextSurfaceProvider && (o.CustomName.Contains(DISPLAY_REMAINING_TAG) || o.CustomName.Contains(DISPLAY_OFFSET_TAG)));

            List<IMyTextSurface> remainingSurfaces = new List<IMyTextSurface>();
            List<IMyTextSurface> offsetSurfaces = new List<IMyTextSurface>();
            foreach (IMyTerminalBlock display in displays) {
                if (display.CustomName.Contains(DISPLAY_REMAINING_TAG)) {
                    int index = GetTextSurfaceIndex(display);
                    remainingSurfaces.Add(((IMyTextSurfaceProvider) display).GetSurface(index));
                    Log("Found remaining display '" + display.CustomName + "'. Using surface #" + index);
                }

                if (display.CustomName.Contains(DISPLAY_OFFSET_TAG)) {
                    int index = GetTextSurfaceIndex(display);
                    offsetSurfaces.Add(((IMyTextSurfaceProvider) display).GetSurface(index));
                    Log("Found offset display '" + display.CustomName + "'. Using surface #" + index);
                }
            }

            if (!valid) {
                return;
            }

            Vector3 move = controller.MoveIndicator;
            Vector2 turn = controller.RotationIndicator;
            float roll = controller.RollIndicator;

            bool hasInput = move.X + move.Y + move.Z + turn.X + turn.Y + roll != 0;

            if (!_InputHandled && hasInput) {
                _InputHandled = true;

                Vector3I pos = projector.ProjectionOffset;

                if (move.X < 0) {
                    if (_SwapAxis) {
                        pos.Z += 1;
                    } else {
                        pos.X += 1;
                    }
                }

                if (move.X > 0) {
                    if (_SwapAxis) {
                        pos.Z -= 1;
                    } else {
                        pos.X -= 1;
                    }
                }

                if (move.Y < 0) {
                    pos.Y += 1;
                }

                if (move.Y > 0) {
                    pos.Y -= 1;
                }

                if (move.Z < 0) {
                    if (_SwapAxis) {
                        pos.X -= 1;
                    } else {
                        pos.Z += 1;
                    }
                }

                if (move.Z > 0) {
                    if (_SwapAxis) {
                        pos.X += 1;
                    } else {
                        pos.Z -= 1;
                    }
                }

                projector.ProjectionOffset = pos;

                Vector3I rot = projector.ProjectionRotation;

                if (turn.X > 0) {
                    rot.X = UpdateRot(rot.X, 1);
                }

                if (turn.X < 0) {
                    rot.X = UpdateRot(rot.X, -1);
                }

                if (turn.Y > 0) {
                    rot.Y = UpdateRot(rot.Y, 1);
                }

                if (turn.Y < 0) {
                    rot.Y = UpdateRot(rot.Y, -1);
                }

                if (roll > 0) {
                    rot.Z = UpdateRot(rot.Z, 1);
                }

                if (roll < 0) {
                    rot.Z = UpdateRot(rot.Z, -1);
                }

                projector.ProjectionRotation = rot;

                projector.UpdateOffsetAndRotation();
            }

            if (_InputHandled && !hasInput) {
                _InputHandled = false;
            }

            if (remainingSurfaces.Count > 0) {
                StringBuilder sb = new StringBuilder();
                // MyDefinitionBase is prohibited but 'var' works ... 
                foreach (var pair in projector.RemainingBlocksPerType.OrderByDescending(o => o.Value)) {
                    sb.Append(pair.Value).Append("x ").Append(GetBlockName(pair.Key.ToString())).AppendLine();
                }

                foreach (IMyTextSurface surface in remainingSurfaces) {
                    surface.WriteText(sb);
                }
            }

            if (offsetSurfaces.Count > 0) {
                StringBuilder sb = new StringBuilder();
                sb.Append("Horizontal offset: ").AppendLine(projector.ProjectionOffset.X.ToString("+#;-#;0"));
                sb.Append("Vertical offset: ").AppendLine(projector.ProjectionOffset.Y.ToString("+#;-#;0"));
                sb.Append("Forward offset: ").AppendLine(projector.ProjectionOffset.Z.ToString("+#;-#;0"));
                sb.Append("Pitch: ").AppendLine(ToDeg(projector.ProjectionRotation.X));
                sb.Append("Yaw: ").AppendLine(ToDeg(projector.ProjectionRotation.Y));
                sb.Append("Roll: ").AppendLine(ToDeg(projector.ProjectionRotation.Z));

                foreach (IMyTextSurface surface in offsetSurfaces) {
                    surface.WriteText(sb);
                }
            }
        }

        private int GetTextSurfaceIndex(IMyTerminalBlock block) {
            IMyTextSurfaceProvider displayProvider = (IMyTextSurfaceProvider) block;
            int surfaceIndex = 0;
            if (displayProvider.SurfaceCount > 1) {
                string indexRaw = block.CustomData.Split('\n').FirstOrDefault(o => o.StartsWith(SURFACE_INDEX_PREFIX));
                if (indexRaw != null) {
                    if (!int.TryParse(indexRaw.Substring(SURFACE_INDEX_PREFIX.Length), out surfaceIndex)) {
                        Echo("Warning: Unable parse display index from '" + indexRaw + "'. First display will be used.");
                    }
                }
            }

            return surfaceIndex;
        }

        private static string GetBlockName(string myDefinitionBaseToString) {
            // transforms text like 'MyObjectBuilder_CargoContainer/SmallBlockMediumContainer' to 'Medium Container'

            string name = myDefinitionBaseToString.Split('/')[1];
            name = name.Replace("Small", "");
            name = name.Replace("Large", "");
            name = name.Replace("Block", "");

            //aka: name = System.Text.RegularExpressions.Regex.Replace(name, "([A-Z])", " $1");
            List<char> chars = name.ToList();
            for (int j = chars.Count - 1; j > 0; j--) {
                if (char.IsUpper(chars[j])) {
                    chars.Insert(j, ' ');
                }
            }

            name = string.Join("", chars);
            return name;
        }

        private static int UpdateRot(int current, int delta) {
            // Rotation valid values are -2, -1, 0, 1, 2 (-180°, -90°, 0°, 90°, 180°)
            current = current + delta;
            if (current > 2) {
                current -= 4;
            }

            if (current < -2) {
                current += 4;
            }

            return current;
        }

        private static string ToDeg(int value) {
            switch (value) {
                case -2: return "-180°";
                case -1: return "-90°";
                case 0:  return "0°";
                case 1:  return "90°";
                case 2:  return "180°";
                // This should never have happened ...
                default: return "HUH: wrong angle: " + value;
            }
        }
        
        private void Log(string message, bool clear = false) {
            Echo(message);
            _Surface.WriteText(message + "\n", !clear);
        }
    }
}
