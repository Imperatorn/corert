﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;

using ILCompiler.DependencyAnalysis;
using ILCompiler.DependencyAnalysis.ReadyToRun;
using ILCompiler.DependencyAnalysisFramework;

using Internal.IL;
using Internal.JitInterface;
using Internal.TypeSystem;
using Internal.TypeSystem.Ecma;

namespace ILCompiler
{
    public sealed class ReadyToRunCodegenCompilationBuilder : CompilationBuilder
    {
        private readonly string _inputFilePath;
        private readonly EcmaModule _inputModule;

        // These need to provide reasonable defaults so that the user can optionally skip
        // calling the Use/Configure methods and still get something reasonable back.
        private KeyValuePair<string, string>[] _ryujitOptions = Array.Empty<KeyValuePair<string, string>>();
        private ILProvider _ilProvider = new ReadyToRunILProvider();

        public ReadyToRunCodegenCompilationBuilder(CompilerTypeSystemContext context, CompilationModuleGroup group, string inputFilePath)
            : base(context, group, new CoreRTNameMangler(new ReadyToRunNodeMangler(), false))
        {
            _inputFilePath = inputFilePath;
            _devirtualizationManager = new DependencyAnalysis.ReadyToRun.DevirtualizationManager(group);

            _inputModule = context.GetModuleFromPath(_inputFilePath);
        }

        public override CompilationBuilder UseBackendOptions(IEnumerable<string> options)
        {
            var builder = new ArrayBuilder<KeyValuePair<string, string>>();

            foreach (string param in options)
            {
                int indexOfEquals = param.IndexOf('=');

                // We're skipping bad parameters without reporting.
                // This is not a mainstream feature that would need to be friendly.
                // Besides, to really validate this, we would also need to check that the config name is known.
                if (indexOfEquals < 1)
                    continue;

                string name = param.Substring(0, indexOfEquals);
                string value = param.Substring(indexOfEquals + 1);

                builder.Add(new KeyValuePair<string, string>(name, value));
            }

            _ryujitOptions = builder.ToArray();

            return this;
        }

        public override CompilationBuilder UseILProvider(ILProvider ilProvider)
        {
            _ilProvider = ilProvider;
            return this;
        }

        protected override ILProvider GetILProvider()
        {
            return _ilProvider;
        }

        public override ICompilation ToCompilation()
        {
            var interopStubManager = new EmptyInteropStubManager(_compilationGroup, _context, new InteropStateManager(_context.GeneratedAssembly));

            ModuleTokenResolver moduleTokenResolver = new ModuleTokenResolver(_compilationGroup, _context);
            SignatureContext signatureContext = new SignatureContext(moduleTokenResolver);

            ReadyToRunCodegenNodeFactory factory = new ReadyToRunCodegenNodeFactory(
                _context,
                _compilationGroup,
                _metadataManager,
                interopStubManager,
                _nameMangler,
                _vtableSliceProvider,
                _dictionaryLayoutProvider,
                moduleTokenResolver,
                signatureContext);

            DependencyAnalyzerBase<NodeFactory> graph = CreateDependencyGraph(factory);

            List<CorJitFlag> corJitFlags = new List<CorJitFlag> { CorJitFlag.CORJIT_FLAG_DEBUG_INFO };

            switch (_optimizationMode)
            {
                case OptimizationMode.None:
                    corJitFlags.Add(CorJitFlag.CORJIT_FLAG_DEBUG_CODE);
                    break;

                case OptimizationMode.PreferSize:
                    corJitFlags.Add(CorJitFlag.CORJIT_FLAG_SIZE_OPT);
                    break;

                case OptimizationMode.PreferSpeed:
                    corJitFlags.Add(CorJitFlag.CORJIT_FLAG_SPEED_OPT);
                    break;

                default:
                    // Not setting a flag results in BLENDED_CODE.
                    break;
            }

            var jitConfig = new JitConfigProvider(corJitFlags, _ryujitOptions);

            return new ReadyToRunCodegenCompilation(
                graph,
                factory,
                _compilationRoots,
                _ilProvider,
                _debugInformationProvider,
                _logger,
                _devirtualizationManager,
                jitConfig,
                _inputFilePath);
        }
    }
}
