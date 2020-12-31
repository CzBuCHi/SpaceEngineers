using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI.Ingame;
using VRage.Game.GUI.TextPanel;

namespace IngameScript {
    internal class Program : MyGridProgram {
        private readonly IMyTextSurface _MyTextSurface;

        public Program() {
            _MyTextSurface = Me.GetSurface(0);
            _MyTextSurface.ContentType = ContentType.TEXT_AND_IMAGE;
            _MyTextSurface.Font = "MONOSPACE";
            _MyTextSurface.WriteText("Batch Rename v1.0\nReady to serve ...");
        }

        public void Main(string argument) {
            _MyTextSurface.WriteText("Batch Rename v1.0\n\nStarting job ...");

            string groupName;
            bool preview;
            List<Token> tokens;
            if (!ParseArgument(argument, out groupName, out preview, out tokens)) {
                _MyTextSurface.WriteText("\nError: Unable to parse argument.", true);
                return;
            }

            if (preview) {
                _MyTextSurface.WriteText("\nRunning job in preview mode only. Blocks will not be renamed.\nFormat string:\n" + argument.Split(';')[1].Substring(1), true);
            }

            IMyBlockGroup group = GridTerminalSystem.GetBlockGroupWithName(groupName);
            if (group == null) {
                _MyTextSurface.WriteText("\nError: Cannot find group with name '" + groupName + "'.", true);
                return;
            }

            List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
            group.GetBlocks(blocks);

            Dictionary<IMyTerminalBlock, string> newNames = new Dictionary<IMyTerminalBlock, string>();

            int index = 1;
            foreach (IMyTerminalBlock block in blocks) {
                string newName = GetBlockNewName(block, index, tokens);
                newNames.Add(block, newName);
                ++index;
            }

            int maxOldNameLength = blocks.Max(o => o.CustomName.Length) + 2; // +2 Apostrophes
            int maxNewNameLength = newNames.Max(o => o.Value.Length) + 2;

            foreach (IMyTerminalBlock block in blocks) {
                string oldName = "'" + block.CustomName + "'";
                if (oldName.Length < maxOldNameLength) {
                    oldName += new string(' ', maxOldNameLength - oldName.Length);
                }

                string newName = "'" + newNames[block] + "'";
                if (newName.Length < maxNewNameLength) {
                    newName = new string(' ', maxNewNameLength - newName.Length) + newName;
                }

                _MyTextSurface.WriteText("\n" + oldName + " -> " + newName, true);
                if (!preview) {
                    block.CustomName = newName;
                }
            }

            if (!preview) {
                _MyTextSurface.WriteText("\n\nRename operation complete.\nWaiting for next job...");
            }
        }

        private string GetBlockNewName(IMyTerminalBlock block, int index, List<Token> tokens) {
            List<string> parts = new List<string>();

            foreach (Token token in tokens) {
                switch (token.Type) {
                    case 'T':
                        parts.Add(token.Value);
                        break;
                    case 'G':
                        parts.Add(GetTokenSubString(block.CubeGrid.CustomName, token));
                        break;
                    case 'N':
                        parts.Add(GetTokenSubString(block.CustomName, token));
                        break;
                    case 'C':
                        int blockIndex = index + (token.Param1 ?? 0);
                        parts.Add(token.Param2 != null ? blockIndex.ToString("D" + token.Param2.Value) : blockIndex.ToString());
                        break;
                }
            }

            return string.Join("", parts);
        }

        private string GetTokenSubString(string fullName, Token token) {
            int? start = token.Param1;
            int? end = token.Param2;

            if (start != null) {
                --start;
            }

            if (end != null && end.Value > fullName.Length) {
                end = null;
            }

            if (start != null) {
                if (start.Value > fullName.Length) {
                    return string.Empty;
                }

                if (end != null) {
                    return fullName.Substring(start.Value, end.Value - start.Value);
                }

                return fullName.Substring(start.Value);
            }

            if (end != null) {
                return fullName.Substring(0, end.Value);
            }

            return fullName;
        }

        private bool ParseArgument(string argument, out string groupName, out bool preview, out List<Token> tokens) {
            groupName = null;
            preview = false;
            tokens = null;

            if (string.IsNullOrEmpty(argument)) {
                return false;
            }

            // get group name
            int index = argument.IndexOf(';');
            if (index == -1) {
                return false;
            }

            groupName = argument.Substring(0, index);

            if (argument[index + 1] == '?') {
                preview = true;
                ++index;
            }

            tokens = new List<Token>();
            int state = 0;   // 0: outside []; 1: first char after [; 2: inside G, N or C block
            char type = 'T'; // 'T', 'G', 'N' or 'C'
            int?[] pars = new int?[2];
            int parIndex = 0;
            int parOffset = 0;

            int last = index + 1;


            for (int i = last; i < argument.Length; i++) {
                char ch = argument[i];

                switch (state) {
                    case 0:
                        if (ch == '[') {
                            if (last != i) {
                                tokens.Add(new Token {
                                    Type = 'T',
                                    Value = DecodeBraces(argument.Substring(last, i - last))
                                });
                            }

                            last = i;
                            state = 1;
                        }

                        break;

                    case 1:
                        if (ch == '[' || ch == ']') {
                            state = 0;
                        } else if (ch == 'G' || ch == 'N' || ch == 'C') {
                            type = ch;
                            last = i;
                            parOffset = i + 1;
                            state = 2;
                        }

                        break;

                    case 2:
                        if (ch == '-') {
                            if (parOffset != i) {
                                pars[0] = int.Parse(argument.Substring(parOffset, i - parOffset));
                            }

                            parIndex = 1;
                            parOffset = i + 1;
                        } else if (ch == ']') {
                            if (parOffset != i) {
                                pars[parIndex] = int.Parse(argument.Substring(parOffset, i - parOffset));
                            }

                            if (type == 'G' || type == 'N') {
                                if (parIndex == 0) {
                                    pars[1] = pars[0];
                                }
                            }

                            tokens.Add(new Token {
                                Type = type,
                                Value = argument.Substring(last, i - last),
                                Param1 = pars[0],
                                Param2 = pars[1]
                            });

                            pars[0] = null;
                            pars[1] = null;
                            parIndex = 0;
                            last = i + 1;
                            state = 0;
                        } else if (!char.IsNumber(ch)) {
                            return false;
                        }

                        break;
                }
            }

            if (last != argument.Length - 1) {
                tokens.Add(new Token {
                    Type = 'T',
                    Value = DecodeBraces(argument.Substring(last))
                });
            }

            // merge string tokens
            for (int i = tokens.Count - 1; i > 1; i--) {
                if (tokens[i].Type == 'T' && tokens[i - 1].Type == 'T') {
                    string value = tokens[i - 1].Value + tokens[i].Value;
                    tokens.RemoveRange(i - 1, 2);
                    tokens.Insert(i - 1, new Token {
                        Type = 'T',
                        Value = value
                    });
                }
            }

            return true;
        }

        private string DecodeBraces(string value) {
            return value.Replace("[[]", "[").Replace("[]]", "]");
        }

        private struct Token {
            public char Type; // 'T', 'G', 'N', 'C'
            public string Value;
            public int? Param1; // G, N: start index; C: first number
            public int? Param2; // G, N: end index, C num of chars
        }
    }
}
