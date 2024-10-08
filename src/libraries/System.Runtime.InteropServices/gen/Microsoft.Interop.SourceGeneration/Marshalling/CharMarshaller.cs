﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Microsoft.Interop
{
    public sealed class Utf16CharMarshaller : IUnboundMarshallingGenerator
    {
        private static readonly ManagedTypeInfo s_nativeType = SpecialTypeInfo.UInt16;

        public ValueBoundaryBehavior GetValueBoundaryBehavior(TypePositionInfo info, StubCodeContext context)
        {
            if (IsPinningPathSupported(info, context))
            {
                return ValueBoundaryBehavior.NativeIdentifier;
            }
            else if (!UsesNativeIdentifier(info, context))
            {
                return ValueBoundaryBehavior.ManagedIdentifier;
            }
            else if (info.IsByRef)
            {
                return ValueBoundaryBehavior.AddressOfNativeIdentifier;
            }

            return ValueBoundaryBehavior.NativeIdentifier;
        }

        public ManagedTypeInfo AsNativeType(TypePositionInfo info)
        {
            Debug.Assert(info.ManagedType is SpecialTypeInfo {SpecialType: SpecialType.System_Char });
            return s_nativeType;
        }

        public SignatureBehavior GetNativeSignatureBehavior(TypePositionInfo info)
        {
            return info.IsByRef ? SignatureBehavior.PointerToNativeType : SignatureBehavior.NativeType;
        }

        public IEnumerable<StatementSyntax> Generate(TypePositionInfo info, StubCodeContext codeContext, StubIdentifierContext context)
        {
            (string managedIdentifier, string nativeIdentifier) = context.GetIdentifiers(info);

            if (IsPinningPathSupported(info, codeContext))
            {
                if (context.CurrentStage == StubIdentifierContext.Stage.Pin)
                {
                    // fixed (char* <pinned> = &<managed>)
                    yield return FixedStatement(
                        VariableDeclaration(
                            PointerType(PredefinedType(Token(SyntaxKind.CharKeyword))),
                            SingletonSeparatedList(
                                VariableDeclarator(Identifier(PinnedIdentifier(info.InstanceIdentifier)))
                                    .WithInitializer(EqualsValueClause(
                                        PrefixUnaryExpression(
                                            SyntaxKind.AddressOfExpression,
                                            IdentifierName(Identifier(managedIdentifier)))
                                    ))
                            )
                        ),
                        // ushort* <native> = (ushort*)<pinned>;
                        LocalDeclarationStatement(
                            VariableDeclaration(PointerType(AsNativeType(info).Syntax),
                                SingletonSeparatedList(
                                    VariableDeclarator(nativeIdentifier)
                                        .WithInitializer(EqualsValueClause(
                                            CastExpression(
                                                PointerType(AsNativeType(info).Syntax),
                                                IdentifierName(PinnedIdentifier(info.InstanceIdentifier))))))))
                    );
                }
                yield break;
            }

            MarshalDirection elementMarshalDirection = MarshallerHelpers.GetMarshalDirection(info, codeContext);

            switch (context.CurrentStage)
            {
                case StubIdentifierContext.Stage.Setup:
                    break;
                case StubIdentifierContext.Stage.Marshal:
                    if (elementMarshalDirection is MarshalDirection.ManagedToUnmanaged or MarshalDirection.Bidirectional)
                    {
                        // There's an implicit conversion from char to ushort,
                        // so we simplify the generated code to just pass the char value directly
                        if (info.IsByRef)
                        {
                            yield return ExpressionStatement(
                                AssignmentExpression(
                                    SyntaxKind.SimpleAssignmentExpression,
                                    IdentifierName(nativeIdentifier),
                                    IdentifierName(managedIdentifier)));
                        }
                    }

                    break;
                case StubIdentifierContext.Stage.Unmarshal:
                    if (elementMarshalDirection is MarshalDirection.UnmanagedToManaged or MarshalDirection.Bidirectional)
                    {
                        yield return ExpressionStatement(
                            AssignmentExpression(
                                SyntaxKind.SimpleAssignmentExpression,
                                IdentifierName(managedIdentifier),
                                CastExpression(
                                    PredefinedType(
                                        Token(SyntaxKind.CharKeyword)),
                                    IdentifierName(nativeIdentifier))));
                    }

                    break;
                default:
                    break;
            }
        }

        public bool UsesNativeIdentifier(TypePositionInfo info, StubCodeContext context)
        {
            MarshalDirection elementMarshalDirection = MarshallerHelpers.GetMarshalDirection(info, context);
            return !IsPinningPathSupported(info, context) && (elementMarshalDirection != MarshalDirection.ManagedToUnmanaged || info.IsByRef);
        }

        private static bool IsPinningPathSupported(TypePositionInfo info, StubCodeContext context)
        {
            return context.SingleFrameSpansNativeContext
                && !context.IsInStubReturnPosition(info)
                && info.IsByRef;
        }

        private static string PinnedIdentifier(string identifier) => $"{identifier}__pinned";
        public ByValueMarshalKindSupport SupportsByValueMarshalKind(ByValueContentsMarshalKind marshalKind, TypePositionInfo info, out GeneratorDiagnostic? diagnostic)
        {
            return ByValueMarshalKindSupportDescriptor.Default.GetSupport(marshalKind, info, out diagnostic);
        }

    }
}
