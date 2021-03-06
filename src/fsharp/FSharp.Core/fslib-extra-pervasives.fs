// Copyright (c) Microsoft Corporation.  All Rights Reserved.  See License.txt in the project root for license information.

namespace Microsoft.FSharp.Core

module ExtraTopLevelOperators =

    open System
    open System.Collections.Generic
    open System.IO
    open System.Diagnostics
    open System.Reflection
    open Microsoft.FSharp
    open Microsoft.FSharp.Core
    open Microsoft.FSharp.Core.Operators
    open Microsoft.FSharp.Text
    open Microsoft.FSharp.Collections
    open Microsoft.FSharp.Control
    open Microsoft.FSharp.Primitives.Basics
    open Microsoft.FSharp.Core.CompilerServices

    let inline checkNonNullNullArg argName arg = 
        match box arg with 
        | null -> nullArg argName 
        | _ -> ()

    let inline checkNonNullInvalidArg argName message arg = 
        match box arg with 
        | null -> invalidArg argName message
        | _ -> ()

    [<CompiledName("CreateSet")>]
    let set l = Collections.Set.ofSeq l

    let dummyArray = [||]
    let inline dont_tail_call f = 
        let result = f ()
        dummyArray.Length |> ignore // pretty stupid way to avoid tail call, would be better if attribute existed, but this should be inlineable by the JIT
        result

    let inline ICollection_Contains<'collection,'item when 'collection :> ICollection<'item>> (collection:'collection) (item:'item) =
        collection.Contains item

    let inline dictImpl (comparer:IEqualityComparer<'SafeKey>) (makeSafeKey:'Key->'SafeKey) (getKey:'SafeKey->'Key) (l:seq<'Key*'T>) = 
        let t = Dictionary comparer
        for (k,v) in l do 
            t.[makeSafeKey k] <- v
        // Give a read-only view of the dictionary
        { new IDictionary<'Key, 'T> with 
                member s.Item 
                    with get x = dont_tail_call (fun () -> t.[makeSafeKey x])
                    and  set x v = raise (NotSupportedException(SR.GetString(SR.thisValueCannotBeMutated)))
                member s.Keys = 
                    let keys = t.Keys
                    { new ICollection<'Key> with 
                          member s.Add(x) = raise (NotSupportedException(SR.GetString(SR.thisValueCannotBeMutated)));
                          member s.Clear() = raise (NotSupportedException(SR.GetString(SR.thisValueCannotBeMutated)));
                          member s.Remove(x) = raise (NotSupportedException(SR.GetString(SR.thisValueCannotBeMutated)));
                          member s.Contains(x) = t.ContainsKey (makeSafeKey x)
                          member s.CopyTo(arr,i) = 
                              let mutable n = 0 
                              for k in keys do 
                                  arr.[i+n] <- getKey k
                                  n <- n + 1
                          member s.IsReadOnly = true
                          member s.Count = keys.Count
                      interface IEnumerable<'Key> with
                            member s.GetEnumerator() = (keys |> Seq.map getKey).GetEnumerator()
                      interface System.Collections.IEnumerable with
                            member s.GetEnumerator() = ((keys |> Seq.map getKey) :> System.Collections.IEnumerable).GetEnumerator() }
                    
                member s.Values = upcast t.Values
                member s.Add(k,v) = raise (NotSupportedException(SR.GetString(SR.thisValueCannotBeMutated)))
                member s.ContainsKey(k) = dont_tail_call (fun () -> t.ContainsKey(makeSafeKey k))
                member s.TryGetValue(k,r) = 
                    let safeKey = makeSafeKey k
                    if t.ContainsKey(safeKey) then (r <- t.[safeKey]; true) else false
                member s.Remove(k : 'Key) = (raise (NotSupportedException(SR.GetString(SR.thisValueCannotBeMutated))) : bool) 
          interface ICollection<KeyValuePair<'Key, 'T>> with 
                member s.Add(x) = raise (NotSupportedException(SR.GetString(SR.thisValueCannotBeMutated)));
                member s.Clear() = raise (NotSupportedException(SR.GetString(SR.thisValueCannotBeMutated)));
                member s.Remove(x) = raise (NotSupportedException(SR.GetString(SR.thisValueCannotBeMutated)));
                member s.Contains(KeyValue(k,v)) = ICollection_Contains t (KeyValuePair<_,_>(makeSafeKey k,v))
                member s.CopyTo(arr,i) = 
                    let mutable n = 0 
                    for (KeyValue(k,v)) in t do 
                        arr.[i+n] <- KeyValuePair<_,_>(getKey k,v)
                        n <- n + 1
                member s.IsReadOnly = true
                member s.Count = t.Count
          interface IEnumerable<KeyValuePair<'Key, 'T>> with
                member s.GetEnumerator() = 
                    (t |> Seq.map (fun (KeyValue(k,v)) -> KeyValuePair<_,_>(getKey k,v))).GetEnumerator()
          interface System.Collections.IEnumerable with
                member s.GetEnumerator() = 
                    ((t |> Seq.map (fun (KeyValue(k,v)) -> KeyValuePair<_,_>(getKey k,v))) :> System.Collections.IEnumerable).GetEnumerator() }

    // We avoid wrapping a StructBox, because under 64 JIT we get some "hard" tailcalls which affect performance
    let dictValueType (l:seq<'Key*'T>) = dictImpl HashIdentity.Structural<'Key> id id l

    // Wrap a StructBox around all keys in case the key type is itself a type using null as a representation
    let dictRefType   (l:seq<'Key*'T>) = dictImpl RuntimeHelpers.StructBox<'Key>.Comparer (fun k -> RuntimeHelpers.StructBox k) (fun sb -> sb.Value) l

    [<CompiledName("CreateDictionary")>]
    let dict (l:seq<'Key*'T>) =
#if FX_RESHAPED_REFLECTION
        if (typeof<'Key>).GetTypeInfo().IsValueType
#else
        if typeof<'Key>.IsValueType
#endif
            then dictValueType l
            else dictRefType   l

    let getArray (vals : seq<'T>) = 
        match vals with
        | :? ('T[]) as arr -> arr
        | _ -> Seq.toArray vals

    [<CompiledName("CreateArray2D")>]
    let array2D (rows : seq<#seq<'T>>) = 
        checkNonNullNullArg "rows" rows
        let rowsArr = getArray rows
        let m = rowsArr.Length
        if m = 0 
        then Array2D.zeroCreate<'T> 0 0 
        else
            checkNonNullInvalidArg "rows" (SR.GetString(SR.nullsNotAllowedInArray)) rowsArr.[0]
            let firstRowArr = getArray rowsArr.[0]
            let n = firstRowArr.Length
            let res = Array2D.zeroCreate<'T> m n
            for j in 0..(n-1) do    
                res.[0,j] <- firstRowArr.[j]
            for i in 1..(m-1) do
              checkNonNullInvalidArg "rows" (SR.GetString(SR.nullsNotAllowedInArray)) rowsArr.[i]
              let rowiArr = getArray rowsArr.[i]
              if rowiArr.Length <> n then invalidArg "vals" (SR.GetString(SR.arraysHadDifferentLengths))
              for j in 0..(n-1) do
                res.[i,j] <- rowiArr.[j]
            res

    // --------------------------------------------------------------------
    // Printf
    // -------------------------------------------------------------------- 

    [<CompiledName("PrintFormatToString")>]
    let sprintf     fp = Printf.sprintf     fp

    [<CompiledName("PrintFormatToStringThenFail")>]
    let failwithf   fp = Printf.failwithf   fp

    [<CompiledName("PrintFormatToTextWriter")>]
    let fprintf (os:TextWriter)  fp = Printf.fprintf os  fp 

    [<CompiledName("PrintFormatLineToTextWriter")>]
    let fprintfn (os:TextWriter) fp = Printf.fprintfn os fp 
    
#if !FX_NO_SYSTEM_CONSOLE
    [<CompiledName("PrintFormat")>]
    let printf      fp = Printf.printf      fp 

    [<CompiledName("PrintFormatToError")>]
    let eprintf     fp = Printf.eprintf     fp 

    [<CompiledName("PrintFormatLine")>]
    let printfn     fp = Printf.printfn     fp 

    [<CompiledName("PrintFormatLineToError")>]
    let eprintfn    fp = Printf.eprintfn    fp 
#endif

    [<CompiledName("FailWith")>]
    let failwith s = raise (Failure s)

    [<CompiledName("DefaultAsyncBuilder")>]
    let async = new Microsoft.FSharp.Control.AsyncBuilder()

    [<CompiledName("ToSingle")>]
    let inline single x = float32 x

    [<CompiledName("ToDouble")>]
    let inline double x = float x

    [<CompiledName("ToByte")>]
    let inline uint8 x = byte x

    [<CompiledName("ToSByte")>]
    let inline int8 x = sbyte x

    module Checked = 

        [<CompiledName("ToByte")>]
        let inline uint8 x = Checked.byte x

        [<CompiledName("ToSByte")>]
        let inline int8 x = Checked.sbyte x

    [<CompiledName("SpliceExpression")>]
    let (~%) (_:Microsoft.FSharp.Quotations.Expr<'a>) : 'a = raise <| InvalidOperationException(SR.GetString(SR.firstClassUsesOfSplice)) 

    [<CompiledName("SpliceUntypedExpression")>]
    let (~%%) (_: Microsoft.FSharp.Quotations.Expr) : 'a = raise <| InvalidOperationException (SR.GetString(SR.firstClassUsesOfSplice)) 

    [<assembly: AutoOpen("Microsoft.FSharp")>]
    [<assembly: AutoOpen("Microsoft.FSharp.Core.LanguagePrimitives.IntrinsicOperators")>]
    [<assembly: AutoOpen("Microsoft.FSharp.Core")>]
    [<assembly: AutoOpen("Microsoft.FSharp.Collections")>]
    [<assembly: AutoOpen("Microsoft.FSharp.Control")>]
    [<assembly: AutoOpen("Microsoft.FSharp.Linq.QueryRunExtensions.LowPriority")>]
    [<assembly: AutoOpen("Microsoft.FSharp.Linq.QueryRunExtensions.HighPriority")>]
    do()

    [<CompiledName("LazyPattern")>]
    let (|Lazy|) (x:Lazy<_>) = x.Force()


    let query = Microsoft.FSharp.Linq.QueryBuilder()


namespace Microsoft.FSharp.Core.CompilerServices

    open System
    open System.Reflection
    open System.Linq.Expressions
    open System.Collections.Generic
    open Microsoft.FSharp.Core


    /// <summary>Represents the product of two measure expressions when returned as a generic argument of a provided type.</summary>
    [<Sealed>]
    type MeasureProduct<'Measure1, 'Measure2>() = class end

    /// <summary>Represents the inverse of a measure expressions when returned as a generic argument of a provided type.</summary>
    [<Sealed>]
    type MeasureInverse<'Measure>  = class end

    /// <summary>Represents the '1' measure expression when returned as a generic argument of a provided type.</summary>
    [<Sealed>]
    type MeasureOne  = class end

    [<AttributeUsage(AttributeTargets.Class, AllowMultiple = false)>]
    type TypeProviderAttribute() =
        inherit System.Attribute()

    [<AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)>]
    type TypeProviderAssemblyAttribute(assemblyName : string) = 
        inherit System.Attribute()
        new () = TypeProviderAssemblyAttribute(null)
        member __.AssemblyName = assemblyName

    [<AttributeUsage(AttributeTargets.All, AllowMultiple = false)>]
    type TypeProviderXmlDocAttribute(commentText: string) = 
        inherit System.Attribute()
        member __.CommentText = commentText

    [<AttributeUsage(AttributeTargets.All, AllowMultiple = false)>]
    type TypeProviderDefinitionLocationAttribute() = 
        inherit System.Attribute()
        let mutable filePath : string = null
        let mutable line : int = 0
        let mutable column : int = 0
        member this.FilePath with get() = filePath and set v = filePath <- v
        member this.Line with get() = line and set v = line <- v
        member this.Column with get() = column and set v = column <- v

    [<AttributeUsage(AttributeTargets.Class ||| AttributeTargets.Interface ||| AttributeTargets.Struct ||| AttributeTargets.Delegate, AllowMultiple = false)>]
    type TypeProviderEditorHideMethodsAttribute() = 
        inherit System.Attribute()

    /// <summary>Additional type attribute flags related to provided types</summary>
    type TypeProviderTypeAttributes =
        | SuppressRelocate = 0x80000000
        | IsErased = 0x40000000

    type TypeProviderConfig( systemRuntimeContainsType : string -> bool ) =
        let mutable resolutionFolder      : string   = null  
        let mutable runtimeAssembly       : string   = null  
        let mutable referencedAssemblies  : string[] = null  
        let mutable temporaryFolder       : string   = null  
        let mutable isInvalidationSupported : bool   = false 
        let mutable useResolutionFolderAtRuntime : bool = false
        let mutable systemRuntimeAssemblyVersion : System.Version = null
        member this.ResolutionFolder         with get() = resolutionFolder        and set v = resolutionFolder <- v
        member this.RuntimeAssembly          with get() = runtimeAssembly         and set v = runtimeAssembly <- v
        member this.ReferencedAssemblies     with get() = referencedAssemblies    and set v = referencedAssemblies <- v
        member this.TemporaryFolder          with get() = temporaryFolder         and set v = temporaryFolder <- v
        member this.IsInvalidationSupported  with get() = isInvalidationSupported and set v = isInvalidationSupported <- v
        member this.IsHostedExecution with get() = useResolutionFolderAtRuntime and set v = useResolutionFolderAtRuntime <- v
        member this.SystemRuntimeAssemblyVersion  with get() = systemRuntimeAssemblyVersion and set v = systemRuntimeAssemblyVersion <- v
        member this.SystemRuntimeContainsType (typeName : string) = systemRuntimeContainsType typeName

    type IProvidedNamespace =
        abstract NamespaceName : string
        abstract GetNestedNamespaces : unit -> IProvidedNamespace[] 
        abstract GetTypes : unit -> Type[] 
        abstract ResolveTypeName : typeName: string -> Type


    type ITypeProvider =
        inherit System.IDisposable
        abstract GetNamespaces : unit -> IProvidedNamespace[] 
        abstract GetStaticParameters : typeWithoutArguments:Type -> ParameterInfo[] 
        abstract ApplyStaticArguments : typeWithoutArguments:Type * typePathWithArguments:string[] * staticArguments:obj[] -> Type 
        abstract GetInvokerExpression : syntheticMethodBase:MethodBase * parameters:Microsoft.FSharp.Quotations.Expr[] -> Microsoft.FSharp.Quotations.Expr

        [<CLIEvent>]
        abstract Invalidate : Microsoft.FSharp.Control.IEvent<System.EventHandler, System.EventArgs>
        abstract GetGeneratedAssemblyContents : assembly:System.Reflection.Assembly -> byte[]

    type ITypeProvider2 =
        abstract GetStaticParametersForMethod : methodWithoutArguments:MethodBase -> ParameterInfo[] 
        abstract ApplyStaticArgumentsForMethod : methodWithoutArguments:MethodBase * methodNameWithArguments:string * staticArguments:obj[] -> MethodBase

