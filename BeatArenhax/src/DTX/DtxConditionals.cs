using System;
using System.Collections.Generic;
using System.Globalization;

namespace BeatArenhax.DTX
{
    internal sealed class DtxConditionals
    {
        private sealed class Branch
        {
            public bool ParentActive;
            public bool AnyMatch;
            public bool ElseSeen;
        }

        private readonly Stack<Branch> _branches = new();
        private readonly Stack<int> _randomValues = new();
        private int _randomValue = 1;

        public bool Active { get; private set; } = true;

        public bool Handle(string key, string value, Random random, List<string> warnings)
        {
            int comment = value.IndexOf(';');
            if (comment >= 0) value = value[..comment].Trim();

            switch (key)
            {
                case "RANDOM":
                    _randomValues.Push(_randomValue);
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int limit) && limit > 0)
                    {
                        if (Active) _randomValue = random.Next(1, limit + 1);
                    }
                    else
                    {
                        warnings.Add("Invalid #RANDOM value: " + value);
                    }
                    return true;

                case "SETRANDOM":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int setValue) && setValue > 0)
                    {
                        if (Active) _randomValue = setValue;
                    }
                    else
                    {
                        warnings.Add("Invalid #SETRANDOM value: " + value);
                    }
                    return true;

                case "ENDRANDOM":
                    if (_randomValues.Count == 0) warnings.Add("#ENDRANDOM has no matching #RANDOM");
                    else _randomValue = _randomValues.Pop();
                    return true;

                case "IF":
                    BeginBranch(value, warnings);
                    return true;

                case "ELSEIF":
                    ElseIf(value, warnings);
                    return true;

                case "ELSE":
                    Else(warnings);
                    return true;

                case "ENDIF":
                    EndIf(warnings);
                    return true;

                case "SWITCH":
                case "CASE":
                case "ENDSWITCH":
                    warnings.Add("Unsupported DTX directive #" + key + "; directive preserved but not evaluated");
                    return true;

                default:
                    return false;
            }
        }

        public void Complete(List<string> warnings)
        {
            if (_branches.Count > 0) warnings.Add("DTX conditional block is missing #ENDIF");
            if (_randomValues.Count > 0) warnings.Add("DTX random block is missing #ENDRANDOM");
        }

        private void BeginBranch(string value, List<string> warnings)
        {
            Branch branch = new() { ParentActive = Active };
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int expected))
            {
                warnings.Add("Invalid #IF value: " + value);
                _branches.Push(branch);
                Active = false;
                return;
            }
            branch.AnyMatch = branch.ParentActive && expected == _randomValue;
            _branches.Push(branch);
            Active = branch.AnyMatch;
        }

        private void ElseIf(string value, List<string> warnings)
        {
            if (_branches.Count == 0)
            {
                warnings.Add("#ELSEIF has no matching #IF");
                return;
            }
            Branch branch = _branches.Peek();
            if (branch.ElseSeen)
            {
                warnings.Add("#ELSEIF appears after #ELSE");
                Active = false;
                return;
            }
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int expected))
            {
                warnings.Add("Invalid #ELSEIF value: " + value);
                Active = false;
                return;
            }
            Active = branch.ParentActive && !branch.AnyMatch && expected == _randomValue;
            branch.AnyMatch |= Active;
        }

        private void Else(List<string> warnings)
        {
            if (_branches.Count == 0)
            {
                warnings.Add("#ELSE has no matching #IF");
                return;
            }
            Branch branch = _branches.Peek();
            if (branch.ElseSeen)
            {
                warnings.Add("Duplicate #ELSE");
                Active = false;
                return;
            }
            branch.ElseSeen = true;
            Active = branch.ParentActive && !branch.AnyMatch;
            branch.AnyMatch |= Active;
        }

        private void EndIf(List<string> warnings)
        {
            if (_branches.Count == 0)
            {
                warnings.Add("#ENDIF has no matching #IF");
                return;
            }
            Active = _branches.Pop().ParentActive;
        }
    }
}
