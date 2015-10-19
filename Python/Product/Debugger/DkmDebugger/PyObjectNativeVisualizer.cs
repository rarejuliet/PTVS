﻿/* ****************************************************************************
 *
 * Copyright (c) Microsoft Corporation. 
 *
 * This source code is subject to terms and conditions of the Apache License, Version 2.0. A 
 * copy of the license can be found in the License.html file at the root of this distribution. If 
 * you cannot locate the Apache License, Version 2.0, please send an email to 
 * vspython@microsoft.com. By using this source code in any fashion, you are agreeing to be bound 
 * by the terms of the Apache License, Version 2.0.
 *
 * You must not remove this notice, or any other, from this software.
 *
 * ***************************************************************************/

using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.Dia;
using Microsoft.PythonTools.DkmDebugger.Proxies.Structs;
using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.Evaluation;
using Microsoft.VisualStudio.Debugger.Evaluation.IL;

namespace Microsoft.PythonTools.DkmDebugger {
    /// <summary>
    /// A custom natvis visualizer for PyObject that replaces it with the corresponding Python visualizer if Python runtime is loaded in the process.
    /// </summary>
    internal class PyObjectNativeVisualizer : DkmDataItem {
        private DkmEvaluationResult GetPythonView(DkmVisualizedExpression visualizedExpression) {
            var stackFrame = visualizedExpression.StackFrame;
            var process = stackFrame.Process;
            var pythonRuntime = process.GetPythonRuntimeInstance();
            if (pythonRuntime == null) {
                return null;
            }

            var home = visualizedExpression.ValueHome as DkmPointerValueHome;
            if (home == null) {
                Debug.Fail("PythonViewNativeVisualizer given a visualized expression that has a non-DkmPointerValueHome home.");
                return null;
            } else if (home.Address == 0) {
                return null;
            }

            var exprEval = process.GetDataItem<ExpressionEvaluator>();
            if (exprEval == null) {
                Debug.Fail("PythonViewNativeVisualizer failed to obtain an instance of ExpressionEvaluator.");
                return null;
            }

            string cppTypeName = null;
            var childExpr = visualizedExpression as DkmChildVisualizedExpression;
            if (childExpr != null) {
                var evalResult = childExpr.EvaluationResult as DkmSuccessEvaluationResult;
                cppTypeName = evalResult.Type;
            } else {
                object punkTypeSymbol;
                visualizedExpression.GetSymbolInterface(typeof(IDiaSymbol).GUID, out punkTypeSymbol);
                using (ComPtr.Create(punkTypeSymbol)) {
                    var typeSymbol = punkTypeSymbol as IDiaSymbol;
                    if (typeSymbol != null) {
                        cppTypeName = typeSymbol.name;
                    }
                }
            }

            PyObject objRef;
            try {
                objRef = PyObject.FromAddress(process, home.Address);
            } catch {
                return null;
            }

            var pyEvalResult = new PythonEvaluationResult(objRef, "[Python view]")
            {
                Category = DkmEvaluationResultCategory.Property,
                AccessType = DkmEvaluationResultAccessType.Private
            };

            var inspectionContext = visualizedExpression.InspectionContext;
            CppExpressionEvaluator cppEval;
            try {
                cppEval = new CppExpressionEvaluator(inspectionContext, stackFrame);
            } catch {
                return null;
            }

            var pythonContext = DkmInspectionContext.Create(visualizedExpression.InspectionSession, pythonRuntime, stackFrame.Thread,
                inspectionContext.Timeout, inspectionContext.EvaluationFlags, inspectionContext.FuncEvalFlags, inspectionContext.Radix,
                DkmLanguage.Create("Python", new DkmCompilerId(Guids.MicrosoftVendorGuid, Guids.PythonLanguageGuid)), null);
            try {
                return exprEval.CreatePyObjectEvaluationResult(pythonContext, stackFrame, null, pyEvalResult, cppEval, cppTypeName, hasCppView: true);
            } catch {
                return null;
            }
        }

        // In VS 2015+, the injection of the child [Python view] node is handled in the PythonDkm.natvis, and the visualizer is only responsible
        // for producing a DkmEvaluationResult for that node.

        public void EvaluateVisualizedExpression(DkmVisualizedExpression visualizedExpression, out DkmEvaluationResult resultObject) {
            resultObject = GetPythonView(visualizedExpression);
            if (resultObject == null) {
                resultObject = DkmFailedEvaluationResult.Create(
                    visualizedExpression.InspectionContext, visualizedExpression.StackFrame, "[Python view]",
                    null, "Python view is unavailable for this object", DkmEvaluationResultFlags.Invalid, null);
            }
        }

        public void GetChildren(DkmVisualizedExpression visualizedExpression, int initialRequestSize, DkmInspectionContext inspectionContext, out DkmChildVisualizedExpression[] initialChildren, out DkmEvaluationResultEnumContext enumContext) {
            throw new NotImplementedException();
        }

        public void GetItems(DkmVisualizedExpression visualizedExpression, DkmEvaluationResultEnumContext enumContext, int startIndex, int count, out DkmChildVisualizedExpression[] items) {
            throw new NotImplementedException();
        }

        public string GetUnderlyingString(DkmVisualizedExpression visualizedExpression) {
            throw new NotImplementedException();
        }

        public void SetValueAsString(DkmVisualizedExpression visualizedExpression, string value, int timeout, out string errorText) {
            throw new NotImplementedException();
        }

        public void UseDefaultEvaluationBehavior(DkmVisualizedExpression visualizedExpression, out bool useDefaultEvaluationBehavior, out DkmEvaluationResult defaultEvaluationResult) {
            useDefaultEvaluationBehavior = true;
            defaultEvaluationResult = GetPythonView(visualizedExpression);
            if (defaultEvaluationResult == null) {
                throw new NotSupportedException();
            }
        }

        public DkmILEvaluationResult[] Execute(DkmILExecuteIntrinsic executeIntrinsic, DkmILContext iLContext, DkmCompiledILInspectionQuery inspectionQuery, DkmILEvaluationResult[] arguments, ReadOnlyCollection<DkmCompiledInspectionQuery> subroutines, out DkmILFailureReason failureReason) {
            var pythonRuntime = iLContext.StackFrame.Process.GetPythonRuntimeInstance();

            // The mapping between functions and IDs is defined in PythonDkm.natvis.
            switch (executeIntrinsic.Id) {
                case 1: // PTVS_ShowPythonViewNodes
                    failureReason = DkmILFailureReason.None;
                    return new[] { DkmILEvaluationResult.Create(executeIntrinsic.SourceId, new ReadOnlyCollection<byte>(new byte[] {
                        pythonRuntime != null && DebuggerOptions.ShowPythonViewNodes ? (byte)1 : (byte)0
                    }))};

                default:
                    throw new NotSupportedException();
            }
        }
    }
}
