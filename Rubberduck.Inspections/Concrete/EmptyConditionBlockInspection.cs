﻿using System;
using System.Collections.Generic;
using System.Linq;
using Rubberduck.Inspections.Abstract;
using Rubberduck.Parsing.Inspections.Abstract;
using Rubberduck.Parsing.Inspections.Resources;
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using Rubberduck.Parsing.Grammar;
using Rubberduck.Parsing;
using Rubberduck.Parsing.VBA;
using Rubberduck.Inspections.Results;

namespace Rubberduck.Inspections.Concrete
{
    [Flags]
    public enum ConditionBlockToInspect
    {
        NA = 0x0,
        If = 0x1,
        ElseIf = 0x2,
        Else = 0x4,
        All = If | ElseIf | Else
    }

    internal class EmptyConditionBlockInspection : ParseTreeInspectionBase
    {
        public EmptyConditionBlockInspection(RubberduckParserState state,
                                            ConditionBlockToInspect BlockToInspect)
            : base(state, CodeInspectionSeverity.Suggestion)
        {
            _blockToInspect = BlockToInspect;
            _listener = new EmptyConditionBlockListener(BlockToInspect);
        }

        public static ConditionBlockToInspect _blockToInspect { get; private set; }

        public override Type Type => typeof(EmptyConditionBlockInspection);

        public override IEnumerable<IInspectionResult> GetInspectionResults()
        {
            return Listener.Contexts
                .Where(result => !IsIgnoringInspectionResultFor(result.ModuleName, result.Context.Start.Line))
                .Select(result => new QualifiedContextInspectionResult(this,
                                                        InspectionsUI.EmptyConditionBlockInspectionsResultFormat,
                                                        result));
        }

        private IInspectionListener _listener;
        public override IInspectionListener Listener { get { return _listener; } }

        public class EmptyConditionBlockListener : EmptyBlockInspectionListenerBase
        {
            ConditionBlockToInspect _blockToInspect;

            public EmptyConditionBlockListener(ConditionBlockToInspect blockToInspect)
            {
                _blockToInspect = blockToInspect;
            }

            public override void EnterIfStmt([NotNull] VBAParser.IfStmtContext context)
            {
                if (_blockToInspect.HasFlag(ConditionBlockToInspect.If))
                {
                    InspectBlockForExecutableStatements(context.block(), context);
                }
            }

            public override void EnterElseIfBlock([NotNull] VBAParser.ElseIfBlockContext context)
            {
                if (_blockToInspect.HasFlag(ConditionBlockToInspect.ElseIf))
                {
                    InspectBlockForExecutableStatements(context.block(), context);
                }
            }

            public override void EnterSingleLineIfStmt([NotNull] VBAParser.SingleLineIfStmtContext context)
            {
                if (context.ifWithEmptyThen() != null & _blockToInspect.HasFlag(ConditionBlockToInspect.If))
                {
                    AddResult(new QualifiedContext<ParserRuleContext>(CurrentModuleName, context.ifWithEmptyThen()));
                }
            }

            public override void EnterElseBlock([NotNull] VBAParser.ElseBlockContext context)
            {
                if (_blockToInspect.HasFlag(ConditionBlockToInspect.Else))
                {
                    InspectBlockForExecutableStatements(context.block(), context);
                }
            }
        }
    }
}