﻿/*****************************************************************************
 * RasterPropMonitor
 * =================
 * Plugin for Kerbal Space Program
 *
 *  by Mihara (Eugene Medvedev), MOARdV, and other contributors
 * 
 * RasterPropMonitor is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, revision
 * date 29 June 2007, or (at your option) any later version.
 * 
 * RasterPropMonitor is distributed in the hope that it will be useful, but
 * WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY
 * or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU General Public License
 * for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with RasterPropMonitor.  If not, see <http://www.gnu.org/licenses/>.
 ****************************************************************************/
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace JSI
{
    class MathVariable : IComplexVariable
    {
        enum Operator
        {
            NONE,
            ADD,
            SUBTRACT,
            MULTIPLY,
            DIVIDE,
            MAX,
            MIN,
            MAXINDEX,
            MININDEX,
        };

        public readonly string name;
        private List<VariableOrNumber> sourceVariables = new List<VariableOrNumber>();
        private readonly Operator op;
        private readonly bool indexOperator;

        internal MathVariable(ConfigNode node)
        {
            name = node.GetValue("name");

            string[] sources = node.GetValues("sourceVariable");
            for (int i = 0; i < sources.Length; ++i)
            {
                VariableOrNumber sv = VariableOrNumber.Instantiate(sources[i]);
                sourceVariables.Add(sv);
            }

            if (sourceVariables.Count == 0)
            {
                throw new ArgumentException("Did not find any SOURCE_VARIABLE nodes in RPM_CUSTOM_VARIABLE", name);
            }

            string oper = node.GetValue("operator");
            if (oper == Operator.NONE.ToString())
            {
                op = Operator.NONE;
                indexOperator = false;
            }
            else if (oper == Operator.ADD.ToString())
            {
                op = Operator.ADD;
                indexOperator = false;
            }
            else if (oper == Operator.SUBTRACT.ToString())
            {
                op = Operator.SUBTRACT;
                indexOperator = false;
            }
            else if (oper == Operator.MULTIPLY.ToString())
            {
                op = Operator.MULTIPLY;
                indexOperator = false;
            }
            else if (oper == Operator.DIVIDE.ToString())
            {
                op = Operator.DIVIDE;
                indexOperator = false;
            }
            else if (oper == Operator.MAX.ToString())
            {
                op = Operator.MAX;
                indexOperator = false;
            }
            else if (oper == Operator.MIN.ToString())
            {
                op = Operator.MIN;
                indexOperator = false;
            }
            else if (oper == Operator.MAXINDEX.ToString())
            {
                op = Operator.MAXINDEX;
                indexOperator = true;
            }
            else if (oper == Operator.MININDEX.ToString())
            {
                op = Operator.MININDEX;
                indexOperator = true;
            }
            else
            {
                throw new ArgumentException("Found an invalid operator type in RPM_CUSTOM_VARIABLE", oper);
            }
        }

        public object Evaluate(RPMVesselComputer comp)
        {
            if (indexOperator)
            {
                int index = 0;
                float value = 0.0f;
                if (!sourceVariables[0].Get(out value, comp))
                {
                    return 0;
                }

                for (int i = 1; i < sourceVariables.Count; ++i)
                {
                    float operand;
                    if (!sourceVariables[i].Get(out operand, comp))
                    {
                        return 0;
                    }

                    switch (op)
                    {
                        case Operator.MAXINDEX:
                            if (operand > value)
                            {
                                index = i;
                                value = operand;
                            }
                            break;
                        case Operator.MININDEX:
                            if (operand < value)
                            {
                                index = i;
                                value = operand;
                            }
                            break;
                    }
                }

                return index;
            }
            else
            {
                float value = 0.0f;
                if (!sourceVariables[0].Get(out value, comp))
                {
                    return 0.0f;
                }

                for (int i = 1; i < sourceVariables.Count; ++i)
                {
                    float operand;
                    if (!sourceVariables[i].Get(out operand, comp))
                    {
                        return 0.0f;
                    }

                    switch (op)
                    {
                        case Operator.NONE:
                            break;
                        case Operator.ADD:
                            value += operand;
                            break;
                        case Operator.SUBTRACT:
                            value -= operand;
                            break;
                        case Operator.MULTIPLY:
                            value *= operand;
                            break;
                        case Operator.DIVIDE:
                            value /= operand;
                            break;
                        case Operator.MAX:
                            value = Mathf.Max(value, operand);
                            break;
                        case Operator.MIN:
                            value = Mathf.Min(value, operand);
                            break;
                    }
                }

                return value;
            }
        }
    }
}
