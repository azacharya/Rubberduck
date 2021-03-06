﻿using Antlr4.Runtime;
using System.Threading;
using Rubberduck.Parsing.Symbols;
using Rubberduck.Parsing.VBA;
using Rubberduck.VBEditor.SafeComWrappers.Abstract;
using System.Collections.Generic;
using Rubberduck.VBEditor.ComManagement.TypeLibs;
using Rubberduck.VBEditor.Utility;
using Rubberduck.Parsing.UIContext;

namespace Rubberduck.Parsing.PreProcessing
{
    public sealed class VBAPreprocessor : IVBAPreprocessor
    {
        private readonly double _vbaVersion;
        private readonly VBAPrecompilationParser _parser;

        public VBAPreprocessor(double vbaVersion)
        {
            _vbaVersion = vbaVersion;
            _parser = new VBAPrecompilationParser();
        }

        public void PreprocessTokenStream(IVBProject project, string moduleName, CommonTokenStream tokenStream, BaseErrorListener errorListener, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var symbolTable = new SymbolTable<string, IValue>();
            var tree = _parser.Parse(moduleName, tokenStream, errorListener);
            token.ThrowIfCancellationRequested();
            var stream = tokenStream.TokenSource.InputStream;
            var evaluator = new VBAPreprocessorVisitor(symbolTable, new VBAPredefinedCompilationConstants(_vbaVersion), GetUserDefinedCompilationArguments(project), stream, tokenStream);
            var expr = evaluator.Visit(tree);
            var processedTokens = expr.Evaluate(); //This does the actual preprocessing of the token stream as a side effect.
            tokenStream.Reset();
        }

        public Dictionary<string, short> GetUserDefinedCompilationArguments(IVBProject project)
        {
            // for the mocks, just return an empty dictionary for now
            if (project == null) return new Dictionary<string, short>();

            // use the TypeLib API to grab the user defined compilation arguments.  must be obtained on the main thread.
            var providerInst = UiContextProvider.Instance();
            var task = (new UiDispatcher(providerInst)).StartTask(delegate () {
                using (var typeLib = TypeLibWrapper.FromVBProject(project))
                {
                    return typeLib.ConditionalCompilationArguments;
                }
            });
            task.Wait();
            return task.Result;
        }
    }
}
