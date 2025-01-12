﻿module DAstConstruction
open System
open System.Numerics
open System.IO
open DAstTypeDefinition
open FsUtils
open CommonTypes
open DAst
open DAstUtilFunctions
open OutDirectories

let foldMap = Asn1Fold.foldMap

let XER (r:Asn1AcnAst.AstRoot) func (us:State) =
    match r.args.hasXer with
    | true -> func ()
    | false -> XerFunctionDummy, us

let createAsn1ValueFromValueKind (t:Asn1AcnAst.Asn1Type) i (v:Asn1ValueKind)  =
    {Asn1Value.kind = v;  loc  = t.Location; id  = (ReferenceToValue (t.id.ToScopeNodeList,[IMG i]))}

let private mapAcnParameter (r:Asn1AcnAst.AstRoot) (deps:Asn1AcnAst.AcnInsertedFieldDependencies) (l:ProgrammingLanguage) (lm:LanguageMacros) (m:Asn1AcnAst.Asn1Module) (t:Asn1AcnAst.Asn1Type) (prm:AcnGenericTypes.AcnParameter) (us:State) =
    //let funcUpdateStatement, ns1 = DAstACN.getUpdateFunctionUsedInEncoding r deps l m prm.id us
    let ns1 = us
    {
        DastAcnParameter.asn1Type = prm.asn1Type; 
        name = prm.name; 
        loc = prm.loc
        id = prm.id
        c_name = DAstACN.getAcnDeterminantName prm.id
        typeDefinitionBodyWithinSeq = DAstACN.getDeterminantTypeDefinitionBodyWithinSeq r l (Asn1AcnAst.AcnParameterDeterminant prm)

        //funcUpdateStatement00 = funcUpdateStatement 
    }, ns1

let private createAcnChild (r:Asn1AcnAst.AstRoot) (deps:Asn1AcnAst.AcnInsertedFieldDependencies) (l:ProgrammingLanguage) (lm:LanguageMacros) (m:Asn1AcnAst.Asn1Module) (ch:Asn1AcnAst.AcnChild) (us:State) =
    let defOrRef (a:Asn1AcnAst.AcnReferenceToEnumerated) =
        match m.Name.Value = a.modName.Value with
        | true  -> ReferenceToExistingDefinition {ReferenceToExistingDefinition.programUnit = None; typedefName = ToC (r.args.TypePrefix + a.tasName.Value) ; definedInRtl = false}
        | false -> ReferenceToExistingDefinition {ReferenceToExistingDefinition.programUnit = Some (ToC a.modName.Value); typedefName = ToC (r.args.TypePrefix + a.tasName.Value); definedInRtl = false}
    let acnAligment =
        match ch.Type with
        | Asn1AcnAst.AcnInteger  a -> a.acnAligment
        | Asn1AcnAst.AcnBoolean  a -> a.acnAligment
        | Asn1AcnAst.AcnNullType a -> a.acnAligment
        | Asn1AcnAst.AcnReferenceToEnumerated a -> a.acnAligment
        | Asn1AcnAst.AcnReferenceToIA5String a -> a.acnAligment

    let funcBodyEncode, ns1= 
        match ch.Type with
        | Asn1AcnAst.AcnInteger  a -> DAstACN.createAcnIntegerFunction r l Codec.Encode ch.id a us
        | Asn1AcnAst.AcnBoolean  a -> DAstACN.createAcnBooleanFunction r l Codec.Encode ch.id a us
        | Asn1AcnAst.AcnNullType a -> DAstACN.createAcnNullTypeFunction r l Codec.Encode ch.id a us
        | Asn1AcnAst.AcnReferenceToEnumerated a -> DAstACN.createAcnEnumeratedFunction r l Codec.Encode ch.id a (defOrRef a) us
        | Asn1AcnAst.AcnReferenceToIA5String a -> DAstACN.createAcnStringFunction r deps l Codec.Encode ch.id a us
        
    let funcBodyDecode, ns2 = 
        match ch.Type with
        | Asn1AcnAst.AcnInteger  a -> DAstACN.createAcnIntegerFunction r l Codec.Decode ch.id a ns1
        | Asn1AcnAst.AcnBoolean  a -> DAstACN.createAcnBooleanFunction r l Codec.Decode ch.id a ns1
        | Asn1AcnAst.AcnNullType a -> DAstACN.createAcnNullTypeFunction r l Codec.Decode ch.id a ns1
        | Asn1AcnAst.AcnReferenceToEnumerated a -> DAstACN.createAcnEnumeratedFunction r l Codec.Decode ch.id a (defOrRef a) ns1
        | Asn1AcnAst.AcnReferenceToIA5String a -> DAstACN.createAcnStringFunction r deps l Codec.Decode ch.id a ns1
        
    let funcUpdateStatement, ns3 = DAstACN.getUpdateFunctionUsedInEncoding r deps l m ch.id ns2
    let c_name         = DAstACN.getAcnDeterminantName ch.id
    
    let newFuncBody (codec:Codec) (prms:((AcnGenericTypes.RelativePath*AcnGenericTypes.AcnParameter) list)) (p:CallerScope) : (AcnFuncBodyResult option)=
        let funBodyWithState st errCode prms p = 
            let funcBody codec = match codec with Codec.Encode -> funcBodyEncode | Codec.Decode -> funcBodyDecode
            funcBody codec prms p, st
        let retFunc = DAstACN.handleSavePostion funBodyWithState ch.Type.savePosition c_name ch.id l codec prms p
        retFunc emptyState {ErroCode.errCodeName = ""; ErroCode.errCodeValue=0} prms p |> fst
        

    let ret = 
        {
        
            AcnChild.Name  = ch.Name
            id             = ch.id
            c_name         = c_name
            Type           = ch.Type
            typeDefinitionBodyWithinSeq = DAstACN.getDeterminantTypeDefinitionBodyWithinSeq r l (Asn1AcnAst.AcnChildDeterminant ch)
            funcBody = DAstACN.handleAlignemntForAcnTypes r l acnAligment newFuncBody
            funcUpdateStatement = funcUpdateStatement
            Comments = ch.Comments
        }
    AcnChild ret, ns3

type ParentInfoData = unit

let private createInteger (r:Asn1AcnAst.AstRoot) (l:ProgrammingLanguage) (lm:LanguageMacros) (m:Asn1AcnAst.Asn1Module) (pi : Asn1Fold.ParentInfo<ParentInfoData> option) (t:Asn1AcnAst.Asn1Type) (o:Asn1AcnAst.Integer) (us:State) =
    //let typeDefinition = DAstTypeDefinition.createInteger  r l t o us
    let defOrRef =  TL "DAstTypeDefinition" (fun () -> DAstTypeDefinition.createInteger_u r l lm t o us)
    let automaticTestCasesIntValues      = TL "EncodeDecodeTestCase" (fun () -> EncodeDecodeTestCase.IntegerAutomaticTestCaseValues r t o)
    let initialValue        = 
        match automaticTestCasesIntValues with
        | []    -> getValueByUperRange o.uperRange 0I
        | x::_  -> 
            match automaticTestCasesIntValues |> Seq.exists ((=) 0I) with
            |true    -> 0I
            |false   -> x
    let automaticTestCasesValues      = automaticTestCasesIntValues |> List.mapi (fun i x -> createAsn1ValueFromValueKind t i (IntegerValue x)) 
    let initFunction        = TL "DAstInitialize" (fun () -> DAstInitialize.createIntegerInitFunc r l t o defOrRef (IntegerValue initialValue))
    let equalFunction       = TL "DAstEqual" (fun () -> DAstEqual.createIntegerEqualFunction r l lm t o defOrRef)
    let isValidFunction, s1     = TL "DastValidate2" (fun () -> DastValidate2.createIntegerFunction r l t o defOrRef  us)
    let uperEncFunction, s2     = TL "DAstUPer" (fun () -> DAstUPer.createIntegerFunction r l Codec.Encode t o defOrRef None isValidFunction s1)
    let uperDecFunction, s3     = TL "DAstUPer" (fun () -> DAstUPer.createIntegerFunction r l Codec.Decode t o defOrRef None isValidFunction s2)
    let acnEncFunction, s4      = TL "DAstACN" (fun () -> DAstACN.createIntegerFunction r l Codec.Encode t o defOrRef isValidFunction uperEncFunction s3)
    let acnDecFunction, s5      = TL "DAstACN" (fun () -> DAstACN.createIntegerFunction r l Codec.Decode t o defOrRef isValidFunction uperDecFunction s4)

    let uperEncDecTestFunc,s6         = EncodeDecodeTestCase.createUperEncDecFunction r l t defOrRef equalFunction isValidFunction (Some uperEncFunction) (Some uperDecFunction) s5
    let acnEncDecTestFunc ,s7         = EncodeDecodeTestCase.createAcnEncDecFunction r l t defOrRef equalFunction isValidFunction (Some acnEncFunction) (Some acnDecFunction) s6


    let xerEncFunction, s8      = TL "DAstXer" (fun () -> XER r (fun () ->  DAstXer.createIntegerFunction  r l Codec.Encode t o defOrRef isValidFunction s7) s7)
    let xerDecFunction, s9      = TL "DAstXer" (fun () -> XER r (fun () ->  DAstXer.createIntegerFunction  r l Codec.Decode t o defOrRef isValidFunction s8) s8)
    let xerEncDecTestFunc,s10   = TL "DAstXer" (fun () -> EncodeDecodeTestCase.createXerEncDecFunction r l t defOrRef equalFunction isValidFunction xerEncFunction xerDecFunction s9)
    
    let ret =
        {
            Integer.baseInfo    = o
            //typeDefinition      = typeDefinition
            definitionOrRef     = defOrRef
            printValue          = DAstVariables.createIntegerFunction r l t o  
            initialValue        = initialValue
            initFunction        = initFunction
            equalFunction       = equalFunction
            isValidFunction     = isValidFunction
            uperEncFunction     = uperEncFunction
            uperDecFunction     = uperDecFunction 
            acnEncFunction      = acnEncFunction
            acnDecFunction      = acnDecFunction
            uperEncDecTestFunc  = uperEncDecTestFunc
            acnEncDecTestFunc   = acnEncDecTestFunc
            xerEncFunction      = xerEncFunction
            xerDecFunction      = xerDecFunction
            xerEncDecTestFunc   = xerEncDecTestFunc
            automaticTestCasesValues     = automaticTestCasesValues
            constraintsAsn1Str = DAstAsn1.createIntegerFunction r t o
        }
    ((Integer ret),[]), s10

let private createReal (r:Asn1AcnAst.AstRoot) (l:ProgrammingLanguage) (lm:LanguageMacros) (m:Asn1AcnAst.Asn1Module) (pi : Asn1Fold.ParentInfo<ParentInfoData> option) (t:Asn1AcnAst.Asn1Type) (o:Asn1AcnAst.Real) (us:State) =
    //let typeDefinition = DAstTypeDefinition.createReal  r l t o us
    let defOrRef            =  DAstTypeDefinition.createReal_u r l lm t o us
    let equalFunction       = DAstEqual.createRealEqualFunction r l lm t o defOrRef 
    let initialValue        = getValueByUperRange o.uperRange 0.0
    let initFunction        = DAstInitialize.createRealInitFunc r l t o defOrRef (RealValue initialValue)
    let isValidFunction, s1     = DastValidate2.createRealFunction r l t o defOrRef  us
    let uperEncFunction, s2     = DAstUPer.createRealFunction r l Codec.Encode t o defOrRef None isValidFunction s1
    let uperDecFunction, s3     = DAstUPer.createRealFunction r l Codec.Decode t o defOrRef None isValidFunction s2
    let acnEncFunction, s4      = DAstACN.createRealrFunction r l Codec.Encode t o defOrRef isValidFunction uperEncFunction s3
    let acnDecFunction, s5      = DAstACN.createRealrFunction r l Codec.Decode t o defOrRef isValidFunction uperDecFunction s4

    let uperEncDecTestFunc,s6         = EncodeDecodeTestCase.createUperEncDecFunction r l t defOrRef equalFunction isValidFunction (Some uperEncFunction) (Some uperDecFunction) s5
    let acnEncDecTestFunc ,s7         = EncodeDecodeTestCase.createAcnEncDecFunction r l t defOrRef equalFunction isValidFunction (Some acnEncFunction) (Some acnDecFunction) s6
    let automaticTestCasesValues      = EncodeDecodeTestCase.RealAutomaticTestCaseValues r t o |> List.mapi (fun i x -> createAsn1ValueFromValueKind t i (RealValue x)) 
    let xerEncFunction, s8      = XER r (fun () ->   DAstXer.createRealFunction  r l Codec.Encode t o defOrRef isValidFunction s7) s7
    let xerDecFunction, s9      = XER r (fun () ->   DAstXer.createRealFunction  r l Codec.Decode t o defOrRef isValidFunction s8) s8       
    let xerEncDecTestFunc,s10   = EncodeDecodeTestCase.createXerEncDecFunction r l t defOrRef equalFunction isValidFunction xerEncFunction xerDecFunction s9

    let ret =
        {
            Real.baseInfo = o
            //typeDefinition      = typeDefinition
            definitionOrRef     = defOrRef
            printValue          = DAstVariables.createRealFunction r l t o  
            initialValue        = initialValue
            initFunction        = initFunction
            equalFunction       = equalFunction
            isValidFunction     = isValidFunction
            uperEncFunction     = uperEncFunction
            uperDecFunction     = uperDecFunction 
            acnEncFunction      = acnEncFunction
            acnDecFunction      = acnDecFunction
            uperEncDecTestFunc  = uperEncDecTestFunc
            acnEncDecTestFunc   = acnEncDecTestFunc
            xerEncFunction      = xerEncFunction
            xerDecFunction      = xerDecFunction
            automaticTestCasesValues = automaticTestCasesValues
            constraintsAsn1Str = DAstAsn1.createRealFunction r t o
            xerEncDecTestFunc   = xerEncDecTestFunc
        }
    ((Real ret),[]), s10



let private createStringType (r:Asn1AcnAst.AstRoot) (deps:Asn1AcnAst.AcnInsertedFieldDependencies) (l:ProgrammingLanguage) (lm:LanguageMacros) (m:Asn1AcnAst.Asn1Module) (pi : Asn1Fold.ParentInfo<ParentInfoData> option) (t:Asn1AcnAst.Asn1Type) (o:Asn1AcnAst.StringType) (us:State) =
    let newPrms, us0 = t.acnParameters |> foldMap(fun ns p -> mapAcnParameter r deps l lm m t p ns) us
    //let typeDefinition = DAstTypeDefinition.createString  r l t o us0
    
    let defOrRef            =  DAstTypeDefinition.createString_u r l lm t o us0
    //let defOrRef            =  DAstTypeDefinition.createString_u r l t o us0
    
    let equalFunction       = DAstEqual.createStringEqualFunction r l lm t o defOrRef 
    let initialValue        =
        let ch = 
            match o.uperCharSet |> Seq.exists((=) ' ') with
            | true  -> ' '
            | false -> o.uperCharSet |> Seq.find(fun c -> not (Char.IsControl c))
        System.String(ch, int o.minSize.uper)
    let initFunction        = DAstInitialize.createIA5StringInitFunc r l t o defOrRef (StringValue initialValue)
    let isValidFunction, s1     = DastValidate2.createStringFunction r l t o defOrRef  us
    let uperEncFunction, s2     = DAstUPer.createIA5StringFunction r l Codec.Encode t o  defOrRef None isValidFunction s1
    let uperDecFunction, s3     = DAstUPer.createIA5StringFunction r l Codec.Decode t o  defOrRef None isValidFunction s2
    let acnEncFunction, s4      = DAstACN.createStringFunction r deps l Codec.Encode t o defOrRef defOrRef isValidFunction uperEncFunction s3
    let acnDecFunction, s5      = DAstACN.createStringFunction r deps l Codec.Decode t o defOrRef defOrRef isValidFunction uperDecFunction s4
    let uperEncDecTestFunc,s6         = EncodeDecodeTestCase.createUperEncDecFunction r l t defOrRef equalFunction isValidFunction (Some uperEncFunction) (Some uperDecFunction) s5
    let acnEncDecTestFunc ,s7         = EncodeDecodeTestCase.createAcnEncDecFunction r l t defOrRef equalFunction isValidFunction (Some acnEncFunction) (Some acnDecFunction) s6
    let automaticTestCasesValues      = EncodeDecodeTestCase.StringAutomaticTestCaseValues r t o |> List.mapi (fun i x -> createAsn1ValueFromValueKind t i (StringValue x)) 
    let xerEncFunction, s8      = XER r (fun () ->   DAstXer.createIA5StringFunction  r l Codec.Encode t o defOrRef isValidFunction s7) s7
    let xerDecFunction, s9      = XER r (fun () ->   DAstXer.createIA5StringFunction  r l Codec.Decode t o defOrRef isValidFunction s8) s8       
    let xerEncDecTestFunc,s10   = EncodeDecodeTestCase.createXerEncDecFunction r l t defOrRef equalFunction isValidFunction xerEncFunction xerDecFunction s9

    let ret =
        {
            StringType.baseInfo = o
            //typeDefinition      = typeDefinition
            definitionOrRef     = defOrRef
            printValue          = DAstVariables.createStringFunction r l t o  
            initialValue        = initialValue
            initFunction        = initFunction
            equalFunction       = equalFunction
            isValidFunction     = isValidFunction
            uperEncFunction     = uperEncFunction
            uperDecFunction     = uperDecFunction 
            acnEncFunction      = acnEncFunction
            acnDecFunction      = acnDecFunction
            uperEncDecTestFunc  = uperEncDecTestFunc
            acnEncDecTestFunc   = acnEncDecTestFunc
            xerEncFunction      = xerEncFunction
            xerDecFunction      = xerDecFunction
            automaticTestCasesValues = automaticTestCasesValues
            constraintsAsn1Str = DAstAsn1.createStringFunction r t o
            xerEncDecTestFunc   = xerEncDecTestFunc
        }
    (ret,newPrms), s10


let private createOctetString (r:Asn1AcnAst.AstRoot) (deps:Asn1AcnAst.AcnInsertedFieldDependencies) (l:ProgrammingLanguage) (lm:LanguageMacros) (m:Asn1AcnAst.Asn1Module) (pi : Asn1Fold.ParentInfo<ParentInfoData> option) (t:Asn1AcnAst.Asn1Type) (o:Asn1AcnAst.OctetString) (us:State) =
    let newPrms, us0 = t.acnParameters |> foldMap(fun ns p -> mapAcnParameter r deps l lm m t p ns) us
    //let typeDefinition = DAstTypeDefinition.createOctet  r l t o us0
    let defOrRef            =  DAstTypeDefinition.createOctetString_u r l lm t o us0
    let initialValue        =
        [1 .. int o.minSize.uper] |> List.map(fun i -> 0uy)
    let equalFunction       = DAstEqual.createOctetStringEqualFunction r l lm t o defOrRef 
    let printValue          = DAstVariables.createOctetStringFunction r l t o defOrRef 

    let isValidFunction, s1     = DastValidate2.createOctetStringFunction r l t o defOrRef  equalFunction printValue us
    let initFunction        = DAstInitialize.createOctetStringInitFunc r l t o defOrRef (OctetStringValue initialValue) isValidFunction
    let uperEncFunction, s2     = DAstUPer.createOctetStringFunction r l Codec.Encode t o  defOrRef None isValidFunction s1
    let uperDecFunction, s3     = DAstUPer.createOctetStringFunction r l Codec.Decode t o  defOrRef None isValidFunction s2
    let acnEncFunction, s4      = DAstACN.createOctetStringFunction r deps l Codec.Encode t o defOrRef isValidFunction uperEncFunction s3
    let acnDecFunction, s5      = DAstACN.createOctetStringFunction r deps l Codec.Decode t o defOrRef isValidFunction uperDecFunction s4
    let uperEncDecTestFunc,s6         = EncodeDecodeTestCase.createUperEncDecFunction r l t defOrRef equalFunction isValidFunction (Some uperEncFunction) (Some uperDecFunction) s5
    let acnEncDecTestFunc ,s7         = EncodeDecodeTestCase.createAcnEncDecFunction r l t defOrRef equalFunction isValidFunction (Some acnEncFunction) (Some acnDecFunction) s6
    let automaticTestCasesValues      = EncodeDecodeTestCase.OctetStringAutomaticTestCaseValues r t o |> List.mapi (fun i x -> createAsn1ValueFromValueKind t i (OctetStringValue x)) 
    let xerEncFunction, s8      = XER r (fun () ->   DAstXer.createOctetStringFunction  r l Codec.Encode t o defOrRef isValidFunction s7) s7
    let xerDecFunction, s9      = XER r (fun () ->   DAstXer.createOctetStringFunction  r l Codec.Decode t o defOrRef isValidFunction s8) s8       
    let xerEncDecTestFunc,s10   = EncodeDecodeTestCase.createXerEncDecFunction r l t defOrRef equalFunction isValidFunction xerEncFunction xerDecFunction s9
    
    let ret =
        {
            OctetString.baseInfo = o
            //typeDefinition      = typeDefinition
            definitionOrRef     = defOrRef
            printValue          = printValue
            initialValue        = initialValue
            initFunction        = initFunction
            equalFunction       = equalFunction
            isValidFunction     = isValidFunction
            uperEncFunction     = uperEncFunction
            uperDecFunction     = uperDecFunction 
            acnEncFunction      = acnEncFunction
            acnDecFunction      = acnDecFunction
            uperEncDecTestFunc  = uperEncDecTestFunc
            acnEncDecTestFunc   = acnEncDecTestFunc
            xerEncFunction      = xerEncFunction
            xerDecFunction      = xerDecFunction
            automaticTestCasesValues = automaticTestCasesValues
            constraintsAsn1Str = DAstAsn1.createOctetStringFunction r t o
            xerEncDecTestFunc   = xerEncDecTestFunc
        }
    ((OctetString ret),newPrms), s10



let private createNullType (r:Asn1AcnAst.AstRoot) (l:ProgrammingLanguage) (lm:LanguageMacros) (m:Asn1AcnAst.Asn1Module) (pi : Asn1Fold.ParentInfo<ParentInfoData> option) (t:Asn1AcnAst.Asn1Type) (o:Asn1AcnAst.NullType) (us:State) =
    //let typeDefinition = DAstTypeDefinition.createNull  r l t o us
    let defOrRef            =  DAstTypeDefinition.createNull_u r l lm t o us
    let equalFunction       = DAstEqual.createNullTypeEqualFunction r l lm  t o defOrRef
    let initialValue        = ()
    let initFunction        = DAstInitialize.createNullTypeInitFunc r l t o defOrRef (NullValue initialValue)
    let uperEncFunction, s2     = DAstUPer.createNullTypeFunction r l Codec.Encode t o defOrRef None None us
    let uperDecFunction, s3     = DAstUPer.createNullTypeFunction r l Codec.Decode t o defOrRef None None s2
    let acnEncFunction, s4      = DAstACN.createNullTypeFunction r l Codec.Encode t o defOrRef None  s3
    let acnDecFunction, s5      = DAstACN.createNullTypeFunction r l Codec.Decode t o defOrRef None  s4
    let uperEncDecTestFunc,s6         = EncodeDecodeTestCase.createUperEncDecFunction r l t defOrRef equalFunction None (Some uperEncFunction) (Some uperDecFunction) s5
    let acnEncDecTestFunc ,s7         = EncodeDecodeTestCase.createAcnEncDecFunction r l t defOrRef equalFunction None (Some acnEncFunction) (Some acnDecFunction) s6
    let xerEncFunction, s8      = XER r (fun () ->   DAstXer.createNullTypeFunction  r l Codec.Encode t o defOrRef None s7) s7
    let xerDecFunction, s9      = XER r (fun () ->   DAstXer.createNullTypeFunction  r l Codec.Decode t o defOrRef None s8) s8       
    let xerEncDecTestFunc,s10   = EncodeDecodeTestCase.createXerEncDecFunction r l t defOrRef equalFunction None xerEncFunction xerDecFunction s9
    let ret =
        {
            NullType.baseInfo   = o
            //typeDefinition      = typeDefinition
            definitionOrRef     = defOrRef
            printValue          = DAstVariables.createNullTypeFunction r l t o  
            initialValue        = initialValue
            initFunction        = initFunction
            equalFunction       = equalFunction
            uperEncFunction     = uperEncFunction
            uperDecFunction     = uperDecFunction 
            acnEncFunction      = acnEncFunction
            acnDecFunction      = acnDecFunction
            uperEncDecTestFunc  = uperEncDecTestFunc
            acnEncDecTestFunc   = acnEncDecTestFunc
            xerEncFunction      = xerEncFunction
            xerDecFunction      = xerDecFunction
            constraintsAsn1Str = []
            xerEncDecTestFunc   = xerEncDecTestFunc
        }
    ((NullType ret),[]), s10



let private createBitString (r:Asn1AcnAst.AstRoot) (deps:Asn1AcnAst.AcnInsertedFieldDependencies) (l:ProgrammingLanguage) (lm:LanguageMacros) (m:Asn1AcnAst.Asn1Module) (pi : Asn1Fold.ParentInfo<ParentInfoData> option) (t:Asn1AcnAst.Asn1Type) (o:Asn1AcnAst.BitString) (us:State) =
    let newPrms, us0 = t.acnParameters |> foldMap(fun ns p -> mapAcnParameter r deps l lm m t p ns) us
    //let typeDefinition = DAstTypeDefinition.createBitString  r l t o us0
    let defOrRef            =  DAstTypeDefinition.createBitString_u r l lm t o us
    let initialValue        =
        System.String('0', int o.minSize.uper)
        
    let equalFunction       = DAstEqual.createBitStringEqualFunction r l lm t o defOrRef 
    let printValue          = DAstVariables.createBitStringFunction r l t o defOrRef 
    let isValidFunction, s1     = DastValidate2.createBitStringFunction r l t o defOrRef defOrRef equalFunction printValue us
    let initFunction        = DAstInitialize.createBitStringInitFunc r l t o defOrRef (BitStringValue initialValue) isValidFunction

    let uperEncFunction, s2     = DAstUPer.createBitStringFunction r l Codec.Encode t o  defOrRef None isValidFunction s1
    let uperDecFunction, s3     = DAstUPer.createBitStringFunction r l Codec.Decode t o  defOrRef None isValidFunction s2
    let acnEncFunction, s4      = DAstACN.createBitStringFunction r deps l Codec.Encode t o defOrRef isValidFunction uperEncFunction s3
    let acnDecFunction, s5      = DAstACN.createBitStringFunction r deps l Codec.Decode t o defOrRef isValidFunction uperDecFunction s4
    let uperEncDecTestFunc,s6         = EncodeDecodeTestCase.createUperEncDecFunction r l t defOrRef equalFunction isValidFunction (Some uperEncFunction) (Some uperDecFunction) s5
    let acnEncDecTestFunc ,s7         = EncodeDecodeTestCase.createAcnEncDecFunction r l t defOrRef equalFunction isValidFunction (Some acnEncFunction) (Some acnDecFunction) s6
    let automaticTestCasesValues      = EncodeDecodeTestCase.BitStringAutomaticTestCaseValues r t o |> List.mapi (fun i x -> createAsn1ValueFromValueKind t i (BitStringValue x)) 
    let xerEncFunction, s8      = XER r (fun () ->   DAstXer.createBitStringFunction  r l Codec.Encode t o defOrRef isValidFunction s7) s7
    let xerDecFunction, s9      = XER r (fun () ->   DAstXer.createBitStringFunction  r l Codec.Decode t o defOrRef isValidFunction s8) s8       
    let xerEncDecTestFunc,s10   = EncodeDecodeTestCase.createXerEncDecFunction r l t defOrRef equalFunction isValidFunction xerEncFunction xerDecFunction s9
    let ret =
        {
            BitString.baseInfo  = o
            //typeDefinition      = typeDefinition
            definitionOrRef     = defOrRef
            printValue          = printValue
            initialValue        = initialValue
            initFunction        = initFunction
            equalFunction       = equalFunction
            isValidFunction     = isValidFunction
            uperEncFunction     = uperEncFunction
            uperDecFunction     = uperDecFunction 
            acnEncFunction      = acnEncFunction
            acnDecFunction      = acnDecFunction
            uperEncDecTestFunc  = uperEncDecTestFunc
            acnEncDecTestFunc   = acnEncDecTestFunc
            automaticTestCasesValues = automaticTestCasesValues
            constraintsAsn1Str = DAstAsn1.createBitStringFunction r t o
            xerEncFunction      = xerEncFunction
            xerDecFunction      = xerDecFunction
            xerEncDecTestFunc   = xerEncDecTestFunc
        }
    ((BitString ret),newPrms), s10


let private createBoolean (r:Asn1AcnAst.AstRoot) (l:ProgrammingLanguage) (lm:LanguageMacros) (m:Asn1AcnAst.Asn1Module) (pi : Asn1Fold.ParentInfo<ParentInfoData> option) (t:Asn1AcnAst.Asn1Type) (o:Asn1AcnAst.Boolean) (us:State) =
    //let typeDefinition = DAstTypeDefinition.createBoolean  r l t o us
    let defOrRef            =  DAstTypeDefinition.createBoolean_u r l lm t o us
    let equalFunction       = DAstEqual.createBooleanEqualFunction r l lm t o defOrRef 
    let initialValue        = false
    let initFunction        = DAstInitialize.createBooleanInitFunc r l t o defOrRef (BooleanValue initialValue)
    let isValidFunction, s1     = DastValidate2.createBoolFunction r l t o defOrRef us
    let uperEncFunction, s2     = DAstUPer.createBooleanFunction r l Codec.Encode t o defOrRef None isValidFunction s1
    let uperDecFunction, s3     = DAstUPer.createBooleanFunction r l Codec.Decode t o defOrRef None isValidFunction s2
    let acnEncFunction, s4      = DAstACN.createBooleanFunction r l Codec.Encode t o defOrRef None isValidFunction  s3
    let acnDecFunction, s5      = DAstACN.createBooleanFunction r l Codec.Decode t o defOrRef None isValidFunction  s4
    let uperEncDecTestFunc,s6         = EncodeDecodeTestCase.createUperEncDecFunction r l t defOrRef equalFunction isValidFunction (Some uperEncFunction) (Some uperDecFunction) s5
    let acnEncDecTestFunc ,s7         = EncodeDecodeTestCase.createAcnEncDecFunction r l t defOrRef equalFunction isValidFunction (Some acnEncFunction) (Some acnDecFunction) s6
    let automaticTestCasesValues      = EncodeDecodeTestCase.BooleanAutomaticTestCaseValues r t o |> List.mapi (fun i x -> createAsn1ValueFromValueKind t i (BooleanValue x)) 
    let xerEncFunction, s8      = XER r (fun () ->   DAstXer.createBooleanFunction  r l Codec.Encode t o defOrRef isValidFunction s7) s7
    let xerDecFunction, s9      = XER r (fun () ->   DAstXer.createBooleanFunction  r l Codec.Decode t o defOrRef isValidFunction s8) s8       
    let xerEncDecTestFunc,s10   = EncodeDecodeTestCase.createXerEncDecFunction r l t defOrRef equalFunction isValidFunction xerEncFunction xerDecFunction s9
    let ret =
        {
            Boolean.baseInfo    = o
            //typeDefinition      = typeDefinition
            definitionOrRef     = defOrRef
            printValue          = DAstVariables.createBooleanFunction r l t o  
            initialValue        = initialValue
            initFunction        = initFunction
            equalFunction       = equalFunction
            isValidFunction     = isValidFunction
            uperEncFunction     = uperEncFunction
            uperDecFunction     = uperDecFunction 
            acnEncFunction      = acnEncFunction
            acnDecFunction      = acnDecFunction
            uperEncDecTestFunc  = uperEncDecTestFunc
            acnEncDecTestFunc   = acnEncDecTestFunc
            automaticTestCasesValues = automaticTestCasesValues
            constraintsAsn1Str = DAstAsn1.createBoolFunction r t o
            xerEncFunction      = xerEncFunction
            xerDecFunction      = xerDecFunction
            xerEncDecTestFunc   = xerEncDecTestFunc
        }
    ((Boolean ret),[]), s10


let private createEnumerated (r:Asn1AcnAst.AstRoot) (l:ProgrammingLanguage) (lm:LanguageMacros) (m:Asn1AcnAst.Asn1Module) (pi : Asn1Fold.ParentInfo<ParentInfoData> option) (t:Asn1AcnAst.Asn1Type) (o:Asn1AcnAst.Enumerated) (us:State) =
    //let typeDefinition = DAstTypeDefinition.createEnumerated  r l t o us
    let defOrRef            =  DAstTypeDefinition.createEnumerated_u r l lm t o us
    let equalFunction       = DAstEqual.createEnumeratedEqualFunction r l lm t o defOrRef 

    let initialValue  =o.items.Head.Name.Value
    let initFunction        = DAstInitialize.createEnumeratedInitFunc r l t o  defOrRef (EnumValue initialValue)
    let isValidFunction, s1     = DastValidate2.createEnumeratedFunction r l t o defOrRef us
    let uperEncFunction, s2     = DAstUPer.createEnumeratedFunction r l Codec.Encode t o  defOrRef None isValidFunction s1
    let uperDecFunction, s3     = DAstUPer.createEnumeratedFunction r l Codec.Decode t o  defOrRef None isValidFunction s2

    let acnEncFunction, s4      = DAstACN.createEnumeratedFunction r l Codec.Encode t o defOrRef defOrRef isValidFunction uperEncFunction s3
    let acnDecFunction, s5      = DAstACN.createEnumeratedFunction r l Codec.Decode t o defOrRef defOrRef isValidFunction uperDecFunction s4

    let uperEncDecTestFunc,s6         = EncodeDecodeTestCase.createUperEncDecFunction r l t defOrRef equalFunction isValidFunction (Some uperEncFunction) (Some uperDecFunction) s5
    let acnEncDecTestFunc ,s7         = EncodeDecodeTestCase.createAcnEncDecFunction r l t defOrRef equalFunction isValidFunction (Some acnEncFunction) (Some acnDecFunction) s6
    let automaticTestCasesValues      = EncodeDecodeTestCase.EnumeratedAutomaticTestCaseValues r t o |> List.mapi (fun i x -> createAsn1ValueFromValueKind t i (EnumValue x)) 
    let xerEncFunction, s8      = XER r (fun () ->   DAstXer.createEnumeratedFunction  r l Codec.Encode t o defOrRef isValidFunction s7) s7
    let xerDecFunction, s9      = XER r (fun () ->   DAstXer.createEnumeratedFunction  r l Codec.Decode t o defOrRef isValidFunction s8) s8       
    let xerEncDecTestFunc,s10   = EncodeDecodeTestCase.createXerEncDecFunction r l t defOrRef equalFunction isValidFunction xerEncFunction xerDecFunction s9

    let ret =
        {
            Enumerated.baseInfo = o
            //typeDefinition      = typeDefinition
            definitionOrRef     = defOrRef
            printValue          = DAstVariables.createEnumeratedFunction r l t o defOrRef  
            initialValue        = initialValue
            initFunction        = initFunction
            equalFunction       = equalFunction
            isValidFunction     = isValidFunction
            uperEncFunction     = uperEncFunction
            uperDecFunction     = uperDecFunction 
            acnEncFunction      = acnEncFunction
            acnDecFunction      = acnDecFunction
            uperEncDecTestFunc  = uperEncDecTestFunc
            acnEncDecTestFunc   = acnEncDecTestFunc
            xerEncFunction      = xerEncFunction
            xerDecFunction      = xerDecFunction
            automaticTestCasesValues = automaticTestCasesValues
            constraintsAsn1Str = DAstAsn1.createEnumeratedFunction r t o
            xerEncDecTestFunc   = xerEncDecTestFunc
        }
    ((Enumerated ret),[]), s10








let private createObjectIdentifier (r:Asn1AcnAst.AstRoot) (l:ProgrammingLanguage) (lm:LanguageMacros) (m:Asn1AcnAst.Asn1Module) (pi : Asn1Fold.ParentInfo<ParentInfoData> option) (t:Asn1AcnAst.Asn1Type) (o:Asn1AcnAst.ObjectIdentifier) (us:State) =
    //let typeDefinition = DAstTypeDefinition.createEnumerated  r l t o us
    let defOrRef            =  DAstTypeDefinition.createObjectIdentifier_u r l lm t o us
    let equalFunction       = DAstEqual.createObjectIdentifierEqualFunction r l lm t o defOrRef 

    let initialValue  = InternalObjectIdentifierValue([])
    let initFunction        = DAstInitialize.createObjectIdentifierInitFunc r l t o  defOrRef (ObjOrRelObjIdValue initialValue)
    let isValidFunction, s1     = DastValidate2.createObjectIdentifierFunction r l t o defOrRef us
    let uperEncFunction, s2     = DAstUPer.createObjectIdentifierFunction r l Codec.Encode t o  defOrRef None isValidFunction s1
    let uperDecFunction, s3     = DAstUPer.createObjectIdentifierFunction r l Codec.Decode t o  defOrRef None isValidFunction s2

    let acnEncFunction, s4      = DAstACN.createObjectIdentifierFunction r l Codec.Encode t o defOrRef  isValidFunction uperEncFunction s3
    let acnDecFunction, s5      = DAstACN.createObjectIdentifierFunction r l Codec.Decode t o defOrRef  isValidFunction uperDecFunction s4

    let uperEncDecTestFunc,s6         = EncodeDecodeTestCase.createUperEncDecFunction r l t defOrRef equalFunction isValidFunction (Some uperEncFunction) (Some uperDecFunction) s5
    let acnEncDecTestFunc ,s7         = EncodeDecodeTestCase.createAcnEncDecFunction r l t defOrRef equalFunction isValidFunction (Some acnEncFunction) (Some acnDecFunction) s6
    let automaticTestCasesValues      = EncodeDecodeTestCase.ObjectIdentifierAutomaticTestCaseValues r t o |> List.mapi (fun i x -> createAsn1ValueFromValueKind t i (ObjOrRelObjIdValue (InternalObjectIdentifierValue x))) 
    let xerEncFunction, s8      = XER r (fun () ->   DAstXer.createObjectIdentifierFunction  r l Codec.Encode t o defOrRef isValidFunction s7) s7
    let xerDecFunction, s9      = XER r (fun () ->   DAstXer.createObjectIdentifierFunction  r l Codec.Decode t o defOrRef isValidFunction s8) s8       
    let xerEncDecTestFunc,s10   = EncodeDecodeTestCase.createXerEncDecFunction r l t defOrRef equalFunction isValidFunction xerEncFunction xerDecFunction s9

    let ret =
        {
            ObjectIdentifier.baseInfo = o
            //typeDefinition      = typeDefinition
            definitionOrRef     = defOrRef
            printValue          = DAstVariables.createObjectIdentifierFunction r l t o defOrRef  
            initialValue        = initialValue
            initFunction        = initFunction
            equalFunction       = equalFunction
            isValidFunction     = isValidFunction
            uperEncFunction     = uperEncFunction
            uperDecFunction     = uperDecFunction 
            acnEncFunction      = acnEncFunction
            acnDecFunction      = acnDecFunction
            uperEncDecTestFunc  = uperEncDecTestFunc
            acnEncDecTestFunc   = acnEncDecTestFunc
            xerEncFunction      = xerEncFunction
            xerDecFunction      = xerDecFunction
            automaticTestCasesValues = automaticTestCasesValues
            constraintsAsn1Str = DAstAsn1.createObjectIdentifierFunction r t o
            xerEncDecTestFunc   = xerEncDecTestFunc
        }
    ((ObjectIdentifier ret),[]), s10


let private createTimeType (r:Asn1AcnAst.AstRoot) (l:ProgrammingLanguage) (lm:LanguageMacros) (m:Asn1AcnAst.Asn1Module) (pi : Asn1Fold.ParentInfo<ParentInfoData> option) (t:Asn1AcnAst.Asn1Type) (o:Asn1AcnAst.TimeType) (us:State) =
    let defOrRef            =  DAstTypeDefinition.createTimeType_u r l lm t o us
    let equalFunction       = DAstEqual.createTimeTypeEqualFunction r l lm t o defOrRef 

    let initialValue  = (EncodeDecodeTestCase.TimeTypeAutomaticTestCaseValues r t o).Head
    let initFunction        = DAstInitialize.createTimeTypeInitFunc r l t o  defOrRef (TimeValue initialValue)
    let isValidFunction, s1     = DastValidate2.createTimeTypeFunction r l t o defOrRef us
    
    let uperEncFunction, s2     = DAstUPer.createTimeTypeFunction r l Codec.Encode t o  defOrRef None isValidFunction s1
    let uperDecFunction, s3     = DAstUPer.createTimeTypeFunction r l Codec.Decode t o  defOrRef None isValidFunction s2

    let acnEncFunction, s4      = DAstACN.createTimeTypeFunction r l Codec.Encode t o defOrRef  isValidFunction uperEncFunction s3
    let acnDecFunction, s5      = DAstACN.createTimeTypeFunction r l Codec.Decode t o defOrRef  isValidFunction uperDecFunction s4

    let uperEncDecTestFunc,s6         = EncodeDecodeTestCase.createUperEncDecFunction r l t defOrRef equalFunction isValidFunction (Some uperEncFunction) (Some uperDecFunction) s5
    let acnEncDecTestFunc ,s7         = EncodeDecodeTestCase.createAcnEncDecFunction r l t defOrRef equalFunction isValidFunction (Some acnEncFunction) (Some acnDecFunction) s6
    let automaticTestCasesValues      = EncodeDecodeTestCase.TimeTypeAutomaticTestCaseValues r t o |> List.mapi (fun i x -> createAsn1ValueFromValueKind t i (TimeValue x)) 
    let xerEncFunction, s8      = XER r (fun () ->   DAstXer.createTimeTypeFunction  r l Codec.Encode t o defOrRef isValidFunction s7) s7
    let xerDecFunction, s9      = XER r (fun () ->   DAstXer.createTimeTypeFunction  r l Codec.Decode t o defOrRef isValidFunction s8) s8       
    let xerEncDecTestFunc,s10   = EncodeDecodeTestCase.createXerEncDecFunction r l t defOrRef equalFunction isValidFunction xerEncFunction xerDecFunction s9

    let ret =
        {
            TimeType.baseInfo = o
            //typeDefinition      = typeDefinition
            definitionOrRef     = defOrRef
            printValue          = DAstVariables.createTimeTypeFunction r l t o defOrRef  
            initialValue        = initialValue
            initFunction        = initFunction
            equalFunction       = equalFunction
            isValidFunction     = isValidFunction
            uperEncFunction     = uperEncFunction
            uperDecFunction     = uperDecFunction 
            acnEncFunction      = acnEncFunction
            acnDecFunction      = acnDecFunction
            uperEncDecTestFunc  = uperEncDecTestFunc
            acnEncDecTestFunc   = acnEncDecTestFunc
            xerEncFunction      = xerEncFunction
            xerDecFunction      = xerDecFunction
            automaticTestCasesValues = automaticTestCasesValues
            constraintsAsn1Str = DAstAsn1.createTimeTypeFunction r t o
            xerEncDecTestFunc   = xerEncDecTestFunc
        }
    ((TimeType ret),[]), s10










let private createSequenceOf (r:Asn1AcnAst.AstRoot) (deps:Asn1AcnAst.AcnInsertedFieldDependencies) (l:ProgrammingLanguage) (lm:LanguageMacros) (m:Asn1AcnAst.Asn1Module) (pi : Asn1Fold.ParentInfo<ParentInfoData> option) (t:Asn1AcnAst.Asn1Type) (o:Asn1AcnAst.SequenceOf) (childType:Asn1Type, us:State) =
    let newPrms, us0 = t.acnParameters |> foldMap(fun ns p -> mapAcnParameter r deps l lm m t p ns) us
    let defOrRef            =  DAstTypeDefinition.createSequenceOf_u r l lm t o  childType.typeDefintionOrReference us0
    //let typeDefinition = DAstTypeDefinition.createSequenceOf r l t o childType.typeDefinition us0
    let equalFunction       = DAstEqual.createSequenceOfEqualFunction r l lm t o defOrRef childType
    let initialValue =
        [1 .. int o.minSize.uper] |> List.map(fun i -> childType.initialValue) |> List.map(fun x -> {Asn1Value.kind=x;id=ReferenceToValue([],[]);loc=emptyLocation}) 
    let initFunction        = DAstInitialize.createSequenceOfInitFunc r l t o defOrRef childType (SeqOfValue initialValue)
    let isValidFunction, s1     = DastValidate2.createSequenceOfFunction r l t o defOrRef childType equalFunction us0
    let uperEncFunction, s2     = DAstUPer.createSequenceOfFunction r l Codec.Encode t o  defOrRef None isValidFunction childType s1
    let uperDecFunction, s3     = DAstUPer.createSequenceOfFunction r l Codec.Decode t o  defOrRef None isValidFunction childType s2
    let acnEncFunction, s4      = DAstACN.createSequenceOfFunction r deps l Codec.Encode t o defOrRef defOrRef isValidFunction childType s3
    let acnDecFunction, s5      = DAstACN.createSequenceOfFunction r deps l Codec.Decode t o defOrRef defOrRef isValidFunction childType s4
    let uperEncDecTestFunc,s6         = EncodeDecodeTestCase.createUperEncDecFunction r l t defOrRef equalFunction isValidFunction (Some uperEncFunction) (Some uperDecFunction) s5
    let acnEncDecTestFunc ,s7         = EncodeDecodeTestCase.createAcnEncDecFunction r l t defOrRef equalFunction isValidFunction (Some acnEncFunction) (Some acnDecFunction) s6
    let automaticTestCasesValues      = 
        match r.args.generateAutomaticTestCases with
        | true  -> EncodeDecodeTestCase.SequenceOfAutomaticTestCaseValues r t o childType |> List.mapi (fun i x -> createAsn1ValueFromValueKind t i (SeqOfValue x)) 
        | false -> []
    let xerEncFunction, s8      = XER r (fun () ->   DAstXer.createSequenceOfFunction  r l Codec.Encode t o defOrRef isValidFunction childType s7) s7
    let xerDecFunction, s9      = XER r (fun () ->   DAstXer.createSequenceOfFunction  r l Codec.Decode t o defOrRef isValidFunction childType s8) s8       
    let xerEncDecTestFunc,s10   = EncodeDecodeTestCase.createXerEncDecFunction r l t defOrRef equalFunction isValidFunction xerEncFunction xerDecFunction s9
    let ret =
        {
            SequenceOf.baseInfo = o
            childType           = childType
            definitionOrRef     = defOrRef
            printValue          = DAstVariables.createSequenceOfFunction r l t o defOrRef  childType
            //typeDefinition      = typeDefinition
            initialValue        = initialValue 
            initFunction        = initFunction
            equalFunction       = equalFunction
            isValidFunction     = isValidFunction
            uperEncFunction     = uperEncFunction
            uperDecFunction     = uperDecFunction 
            acnEncFunction      = acnEncFunction
            acnDecFunction      = acnDecFunction
            uperEncDecTestFunc  = uperEncDecTestFunc
            acnEncDecTestFunc   = acnEncDecTestFunc
            xerEncFunction      = xerEncFunction
            xerDecFunction      = xerDecFunction
            automaticTestCasesValues = automaticTestCasesValues
            constraintsAsn1Str = DAstAsn1.createSequenceOfFunction r t o
            xerEncDecTestFunc   = xerEncDecTestFunc
        }
    ((SequenceOf ret),newPrms), s10



let private createAsn1Child (r:Asn1AcnAst.AstRoot) (l:ProgrammingLanguage) (lm:LanguageMacros) (m:Asn1AcnAst.Asn1Module) (ch:Asn1AcnAst.Asn1Child) (newChildType : Asn1Type, us:State) =
    let ret = 
        {
        
            Asn1Child.Name     = ch.Name
            _c_name             = ch._c_name
            _ada_name           = ch._ada_name
            Type               = newChildType
            Optionality        = ch.Optionality
            Comments           = ch.Comments |> Seq.toArray
            isEqualBodyStats   = DAstEqual.isEqualBodySequenceChild l lm ch newChildType
            //isValidBodyStats    = DAstValidate.isValidSequenceChild l ch newChildType
        }
    Asn1Child ret, us




let private createSequence (r:Asn1AcnAst.AstRoot) (deps:Asn1AcnAst.AcnInsertedFieldDependencies) (l:ProgrammingLanguage) (lm:LanguageMacros) (m:Asn1AcnAst.Asn1Module) (pi : Asn1Fold.ParentInfo<ParentInfoData> option) (t:Asn1AcnAst.Asn1Type) (o:Asn1AcnAst.Sequence) (children:SeqChildInfo list, us:State) =
    let newPrms, us0 = TL "SQ_mapAcnParameter" (fun () -> t.acnParameters |> foldMap(fun ns p -> mapAcnParameter r deps l lm m t p ns) us)
    //let typeDefinition = DAstTypeDefinition.createSequence r l t o children us0
    let defOrRef            =  TL "SQ_DAstTypeDefinition" (fun () -> DAstTypeDefinition.createSequence_u r l lm t o  children us)
    let equalFunction       = TL "SQ_DAstEqual" (fun () -> DAstEqual.createSequenceEqualFunction r l lm t o defOrRef children)
    let initialValue =
        TL "SQ_initialValue" (fun () -> 
                            children |> 
                            List.choose(fun ch -> 
                                match ch with
                                | Asn1Child o -> Some ({NamedValue.name = o.Name.Value; Value={Asn1Value.kind=o.Type.initialValue;id=ReferenceToValue([],[]);loc=emptyLocation}})
                                | AcnChild  _ -> None))
    let initFunction        = TL "SQ_DAstInitialize" (fun () -> DAstInitialize.createSequenceInitFunc r l t o defOrRef children (SeqValue initialValue))
    let isValidFunction, s1     =  TL "SQ_DAstInitialize" (fun () -> DastValidate2.createSequenceFunction r l t o defOrRef children  us)
        
    let uperEncFunction, s2     = TL "SQ_DAstUPer" (fun () ->DAstUPer.createSequenceFunction r l Codec.Encode t o defOrRef isValidFunction children s1)
    let uperDecFunction, s3     = TL "SQ_DAstUPer" (fun () ->DAstUPer.createSequenceFunction r l Codec.Decode t o defOrRef isValidFunction children s2)
    let acnEncFunction, s4      = TL "SQ_DAstACN" (fun () ->DAstACN.createSequenceFunction r deps l Codec.Encode t o defOrRef  isValidFunction children newPrms s3)
    let acnDecFunction, s5      = TL "SQ_DAstACN" (fun () ->DAstACN.createSequenceFunction r deps l Codec.Decode t o defOrRef  isValidFunction children newPrms s4)
    let uperEncDecTestFunc,s6         = TL "SQ_EncodeDecodeTestCase" (fun () ->EncodeDecodeTestCase.createUperEncDecFunction r l t defOrRef equalFunction isValidFunction (Some uperEncFunction) (Some uperDecFunction) s5)
    let acnEncDecTestFunc ,s7         = TL "SQ_EncodeDecodeTestCase" (fun () ->EncodeDecodeTestCase.createAcnEncDecFunction r l t defOrRef equalFunction isValidFunction (Some acnEncFunction) (Some acnDecFunction) s6)
    let automaticTestCasesValues      = 
        TL "SQ_EncodeDecodeTestCase" (fun () -> 
                                match r.args.generateAutomaticTestCases with
                                | true  -> EncodeDecodeTestCase.SequenceAutomaticTestCaseValues r t o children |> List.mapi (fun i x -> createAsn1ValueFromValueKind t i (SeqValue x)) 
                                | false -> [])
    let xerEncFunction, s8      = TL "SQ_DAstXer" (fun () ->XER r (fun () ->   DAstXer.createSequenceFunction  r l Codec.Encode t o defOrRef isValidFunction children s7) s7)
    let xerDecFunction, s9      = TL "SQ_DAstXer" (fun () ->XER r (fun () ->   DAstXer.createSequenceFunction  r l Codec.Decode t o defOrRef isValidFunction children s8) s8)
    let xerEncDecTestFunc,s10   = TL "SQ_EncodeDecodeTestCase" (fun () ->EncodeDecodeTestCase.createXerEncDecFunction r l t defOrRef equalFunction isValidFunction xerEncFunction xerDecFunction s9)
    let ret =
        {
            Sequence.baseInfo   = o
            children            = children
            //typeDefinition      = typeDefinition
            definitionOrRef     = defOrRef
            printValue          = DAstVariables.createSequenceFunction r l t o defOrRef  children
            initialValue        = initialValue
            initFunction        = initFunction
            equalFunction       = equalFunction
            isValidFunction     = isValidFunction
            uperEncFunction     = uperEncFunction
            uperDecFunction     = uperDecFunction 
            acnEncFunction      = acnEncFunction
            acnDecFunction      = acnDecFunction
            uperEncDecTestFunc  = uperEncDecTestFunc
            acnEncDecTestFunc   = acnEncDecTestFunc
            xerEncFunction      = xerEncFunction
            xerDecFunction      = xerDecFunction
            automaticTestCasesValues = automaticTestCasesValues
            constraintsAsn1Str = DAstAsn1.createSequenceFunction r t o children
            xerEncDecTestFunc   = xerEncDecTestFunc
        }
    ((Sequence ret),newPrms), s10

let private createChoice (r:Asn1AcnAst.AstRoot) (deps:Asn1AcnAst.AcnInsertedFieldDependencies) (l:ProgrammingLanguage) (lm:LanguageMacros) (m:Asn1AcnAst.Asn1Module) (pi : Asn1Fold.ParentInfo<ParentInfoData> option) (t:Asn1AcnAst.Asn1Type) (o:Asn1AcnAst.Choice) (children:ChChildInfo list, us:State) =
    let newPrms, us0 = t.acnParameters |> foldMap(fun ns p -> mapAcnParameter r deps l lm m t p ns) us
    //let typeDefinition = DAstTypeDefinition.createChoice r l t o children us0
    let defOrRef            =  TL "DAstTypeDefinition" (fun () -> DAstTypeDefinition.createChoice_u r l lm t o  children us)
    let equalFunction       = TL "DAstEqual" (fun () -> DAstEqual.createChoiceEqualFunction r l lm t o defOrRef children)
    let initialValue =
        children |> Seq.map(fun o -> {NamedValue.name = o.Name.Value; Value={Asn1Value.kind=o.chType.initialValue;id=ReferenceToValue([],[]);loc=emptyLocation}}) |> Seq.head
    let initFunction        = TL "DAstInitialize" (fun () -> DAstInitialize.createChoiceInitFunc r l t o defOrRef children (ChValue initialValue))
    let isValidFunction, s1     = TL "DastValidate2" (fun () -> DastValidate2.createChoiceFunction r l t o defOrRef defOrRef children None us)
    let uperEncFunction, s2     = TL "DAstUPer" (fun () -> DAstUPer.createChoiceFunction r l Codec.Encode t o  defOrRef None isValidFunction children s1)
    let uperDecFunction, s3     = TL "DAstUPer" (fun () -> DAstUPer.createChoiceFunction r l Codec.Decode t o  defOrRef None isValidFunction children s2)
    let (acnEncFunction, s4),ec      = TL "DAstACN" (fun () -> DAstACN.createChoiceFunction r deps l Codec.Encode t o defOrRef defOrRef isValidFunction children newPrms  s3)
    let (acnDecFunction, s5),_      = TL "DAstACN" (fun () -> DAstACN.createChoiceFunction r deps l Codec.Decode t o defOrRef defOrRef isValidFunction children newPrms  s4)
    let uperEncDecTestFunc,s6         = TL "EncodeDecodeTestCase" (fun () -> EncodeDecodeTestCase.createUperEncDecFunction r l t defOrRef equalFunction isValidFunction (Some uperEncFunction) (Some uperDecFunction) s5)
    let acnEncDecTestFunc ,s7         = TL "EncodeDecodeTestCase" (fun () -> EncodeDecodeTestCase.createAcnEncDecFunction r l t defOrRef equalFunction isValidFunction (Some acnEncFunction) (Some acnDecFunction) s6)
    let automaticTestCasesValues      = 
        match r.args.generateAutomaticTestCases with
        | true  -> TL "EncodeDecodeTestCase" (fun () -> EncodeDecodeTestCase.ChoiceAutomaticTestCaseValues r t o children |> List.mapi (fun i x -> createAsn1ValueFromValueKind t i (ChValue x)) )
        | false -> []
    let xerEncFunction, s8      = TL "DAstXer" (fun () -> XER r (fun () ->   DAstXer.createChoiceFunction  r l Codec.Encode t o defOrRef isValidFunction children s7) s7)
    let xerDecFunction, s9      = TL "DAstXer" (fun () -> XER r (fun () ->   DAstXer.createChoiceFunction  r l Codec.Decode t o defOrRef isValidFunction children s8) s8)
    let xerEncDecTestFunc,s10   = TL "EncodeDecodeTestCase" (fun () -> EncodeDecodeTestCase.createXerEncDecFunction r l t defOrRef equalFunction isValidFunction xerEncFunction xerDecFunction s9)
    let ret =
        {
            Choice.baseInfo     = o
            children            = children
            definitionOrRef     = defOrRef
            printValue          = DAstVariables.createChoiceFunction r l t o  defOrRef children
            //typeDefinition      = typeDefinition
            initialValue        = initialValue
            initFunction        = initFunction
            equalFunction       = equalFunction
            isValidFunction     = isValidFunction
            uperEncFunction     = uperEncFunction
            uperDecFunction     = uperDecFunction 
            acnEncFunction      = acnEncFunction
            acnDecFunction      = acnDecFunction
            uperEncDecTestFunc  = uperEncDecTestFunc
            acnEncDecTestFunc   = acnEncDecTestFunc
            xerEncFunction      = xerEncFunction
            xerDecFunction      = xerDecFunction
            automaticTestCasesValues = automaticTestCasesValues
            constraintsAsn1Str = DAstAsn1.createChoiceFunction r t o children
            xerEncDecTestFunc   = xerEncDecTestFunc
            ancEncClass         = ec
        }
    ((Choice ret),newPrms), s10

let private createChoiceChild (r:Asn1AcnAst.AstRoot) (l:ProgrammingLanguage) (lm:LanguageMacros) (m:Asn1AcnAst.Asn1Module) (ch:Asn1AcnAst.ChChildInfo) (newChildType : Asn1Type, us:State) =
    let typeDefinitionName = 
        let longName = newChildType.id.AcnAbsPath.Tail |> List.rev |> List.tail |> List.rev |> Seq.StrJoin "_"
        ToC2(r.args.TypePrefix + longName.Replace("#","elem"))
    let ret = 
        {
        
            ChChildInfo.Name     = ch.Name
            _c_name             = ch._c_name
            _ada_name           = ch._ada_name
            _present_when_name_private  = ch.present_when_name
            acnPresentWhenConditions = ch.acnPresentWhenConditions
            chType              = newChildType
            Comments            = ch.Comments |> Seq.toArray
            isEqualBodyStats    = DAstEqual.isEqualBodyChoiceChild typeDefinitionName l lm ch newChildType
            //isValidBodyStats    = DAstValidate.isValidChoiceChild l ch newChildType
            Optionality         = ch.Optionality
        }
    ret, us

let private createReferenceType (r:Asn1AcnAst.AstRoot) (deps:Asn1AcnAst.AcnInsertedFieldDependencies) (l:ProgrammingLanguage) (lm:LanguageMacros) (m:Asn1AcnAst.Asn1Module) (pi : Asn1Fold.ParentInfo<ParentInfoData> option) (t:Asn1AcnAst.Asn1Type) (o:Asn1AcnAst.ReferenceType) (newResolvedType:Asn1Type, us:State) =
    let newPrms, us0 = t.acnParameters |> foldMap(fun ns p -> mapAcnParameter r deps l lm m t p ns) us
    let defOrRef            =  DAstTypeDefinition.createReferenceType_u r l lm t o  newResolvedType us
    //let typeDefinition = DAstTypeDefinition.createReferenceType r l t o newResolvedType us
    let equalFunction       = DAstEqual.createReferenceTypeEqualFunction r l lm t o defOrRef newResolvedType
    let initialValue        = {Asn1Value.kind=newResolvedType.initialValue;id=ReferenceToValue([],[]);loc=emptyLocation}
    let initFunction        = DAstInitialize.createReferenceType r l t o defOrRef newResolvedType
    let isValidFunction, s1     = DastValidate2.createReferenceTypeFunction r l t o defOrRef newResolvedType us
    let uperEncFunction, s2     = DAstUPer.createReferenceFunction r l Codec.Encode t o defOrRef isValidFunction newResolvedType s1
    let uperDecFunction, s3     = DAstUPer.createReferenceFunction r l Codec.Decode t o defOrRef isValidFunction newResolvedType s2
    let acnEncFunction, s4      = DAstACN.createReferenceFunction r deps l Codec.Encode t o defOrRef  isValidFunction newResolvedType s3
    let acnDecFunction, s5      = DAstACN.createReferenceFunction r deps l Codec.Decode t o defOrRef  isValidFunction newResolvedType s4
    
    let uperEncDecTestFunc,s6         = EncodeDecodeTestCase.createUperEncDecFunction r l t defOrRef equalFunction isValidFunction (Some uperEncFunction) (Some uperDecFunction) s5
    let acnEncDecTestFunc ,s7         = EncodeDecodeTestCase.createAcnEncDecFunction r l t defOrRef equalFunction isValidFunction acnEncFunction acnDecFunction s6

    let xerEncFunction, s8      = XER r (fun () ->   DAstXer.createReferenceFunction  r l Codec.Encode t o defOrRef isValidFunction newResolvedType s7) s7
    let xerDecFunction, s9      = XER r (fun () ->   DAstXer.createReferenceFunction  r l Codec.Decode t o defOrRef isValidFunction newResolvedType s8) s8       
    let xerEncDecTestFunc,s10   = EncodeDecodeTestCase.createXerEncDecFunction r l t defOrRef equalFunction isValidFunction xerEncFunction xerDecFunction s9

    let ret = 
        {
            ReferenceType.baseInfo = o
            resolvedType        = newResolvedType
            //typeDefinition      = typeDefinition
            definitionOrRef     = defOrRef
            printValue          = DAstVariables.createReferenceTypeFunction r l t o defOrRef newResolvedType
            initialValue        = initialValue
            initFunction        = initFunction
            equalFunction       = equalFunction
            isValidFunction     = isValidFunction
            uperEncFunction     = uperEncFunction
            uperDecFunction     = uperDecFunction 
            acnEncFunction      = acnEncFunction.Value
            acnDecFunction      = acnDecFunction.Value
            uperEncDecTestFunc  = uperEncDecTestFunc
            acnEncDecTestFunc   = acnEncDecTestFunc
            xerEncFunction      = xerEncFunction
            xerDecFunction      = xerDecFunction
            automaticTestCasesValues = newResolvedType.automaticTestCasesValues
            constraintsAsn1Str = newResolvedType.ConstraintsAsn1Str
            xerEncDecTestFunc   = xerEncDecTestFunc
        }
    ((ReferenceType ret),newPrms), s10

let private createType (r:Asn1AcnAst.AstRoot) pi (t:Asn1AcnAst.Asn1Type) ((newKind, newPrms),us) =
    {
        Asn1Type.Kind = newKind
        id            = t.id
        acnAligment   = t.acnAligment
        acnParameters = newPrms 
        Location      = t.Location
        inheritInfo = t.inheritInfo
        typeAssignmentInfo = t.typeAssignmentInfo
        //newTypeDefName = DAstTypeDefinition2.getTypedefName r pi t
    }, us

let private mapType (r:Asn1AcnAst.AstRoot) (deps:Asn1AcnAst.AcnInsertedFieldDependencies) (l:ProgrammingLanguage) (lm:LanguageMacros) (m:Asn1AcnAst.Asn1Module) (t:Asn1AcnAst.Asn1Type, us:State) =
    Asn1Fold.foldType2
        (fun pi t ti us -> TL "createInteger" (fun () -> createInteger r l lm m pi t ti us))
        (fun pi t ti us -> TL "createReal" (fun () -> createReal r l lm m pi t ti us))
        (fun pi t ti us -> 
            let (strtype, prms), ns = TL "createStringType" (fun () -> createStringType r deps l lm m pi t ti us)
            ((IA5String strtype),prms), ns)
        (fun pi t ti us -> 
            let (strtype, prms), ns = TL "createStringType" (fun () -> createStringType r deps l lm m pi t ti us)
            ((IA5String strtype),prms), ns)
        (fun pi t ti us -> TL "createOctetString" (fun () -> createOctetString r deps l lm m pi t ti us))
        (fun pi t ti us -> TL "createTimeType" (fun () -> createTimeType r l lm m pi t ti us))
        (fun pi t ti us -> TL "createNullType" (fun () -> createNullType r l lm m pi t ti us))
        (fun pi t ti us -> TL "createBitString" (fun () -> createBitString r deps l lm m pi t ti us))
        
        (fun pi t ti us -> TL "createBoolean" (fun () -> createBoolean r l lm m pi t ti us))
        (fun pi t ti us -> TL "createEnumerated" (fun () -> createEnumerated r l lm m pi t ti us))
        (fun pi t ti us -> TL "createObjectIdentifier" (fun () -> createObjectIdentifier r l lm  m pi t ti us))

        (fun pi t ti newChild -> TL "createSequenceOf" (fun () -> createSequenceOf r deps l lm m pi t ti newChild))

        (fun pi t ti newChildren -> TL "createSequence" (fun () -> createSequence r deps l lm m pi t ti newChildren))
        (fun ch newChild -> TL "createAsn1Child" (fun () -> createAsn1Child r l lm m ch newChild))
        (fun ch us -> TL "createAcnChild" (fun () -> createAcnChild r deps l lm m ch us))
        

        (fun pi t ti newChildren -> TL "createChoice" (fun () -> createChoice r deps l lm m pi t ti newChildren))
        (fun ch newChild -> TL "createChoiceChild" (fun () -> createChoiceChild r l lm m ch newChild))

        (fun pi t ti newBaseType -> 
            TL "createReferenceType" (fun () -> createReferenceType r deps l lm m pi t ti newBaseType))

        (fun pi t ((newKind, newPrms),us)        -> TL "createType" (fun () -> createType r pi t ((newKind, newPrms),us)))

        (fun pi t ti us -> (),us)
        (fun pi t ti us -> (),us)
        (fun pi t ti us -> (),us)


        None
        t
        us 
        

let private mapTypeId (r:Asn1AcnAst.AstRoot)  (t:Asn1AcnAst.Asn1Type) =
    Asn1Fold.foldType2
        (fun pi t ti us -> [t.id], us)
        (fun pi t ti us -> [t.id], us)
        (fun pi t ti us -> [t.id], us)
        (fun pi t ti us -> [t.id], us)
        (fun pi t ti us -> [t.id], us)
        (fun pi t ti us -> [t.id], us)
        (fun pi t ti us -> [t.id], us)
        (fun pi t ti us -> [t.id], us)
        
        (fun pi t ti us -> [t.id], us)
        (fun pi t ti us -> [t.id], us)
        (fun pi t ti us -> [t.id], us)

        (fun pi t ti (newChild,_) -> newChild@[t.id],0)

        (fun pi t ti (newChildren,_) -> (newChildren|> List.collect id)@[t.id], 0)
        (fun ch (newChild,_) -> newChild, 0)
        (fun ch us -> [ch.id], us)
        

        (fun pi t ti (newChildren,_) -> (newChildren|> List.collect id)@[t.id], 0)
        (fun ch (newChild,_) -> newChild, 0)

        (fun pi t ti newBaseType -> [t.id], 0)

        (fun pi t (newKind,_)        -> newKind, 0)

        (fun pi t ti us -> (),us)
        (fun pi t ti us -> (),us)
        (fun pi t ti us -> (),us)


        None
        t
        0 |> fst




let private mapTas (r:Asn1AcnAst.AstRoot) (deps:Asn1AcnAst.AcnInsertedFieldDependencies) (l:ProgrammingLanguage) (lm:LanguageMacros) (m:Asn1AcnAst.Asn1Module) (tas:Asn1AcnAst.TypeAssignment) (us:State)=
    let newType, ns = TL "mapType" (fun () -> mapType r deps l lm m (tas.Type, us))
    {
        TypeAssignment.Name = tas.Name
        c_name = tas.c_name
        ada_name = tas.ada_name
        Type = newType
        Comments = tas.Comments |> Seq.toArray
    },ns


let private mapVas (r:Asn1AcnAst.AstRoot) (allNewTypeAssignments : (Asn1Module*TypeAssignment) list) (deps:Asn1AcnAst.AcnInsertedFieldDependencies)  (l:ProgrammingLanguage) (lm:LanguageMacros) (m:Asn1AcnAst.Asn1Module) (vas:Asn1AcnAst.ValueAssignment) (us:State)=
    let newType, ns = 
        match vas.Type.Kind with
        | Asn1AcnAst.ReferenceType ref ->
            //let aaa = allNewTypeAssignments |> Seq.map(fun tz -> sprintf "%s.%s")
            match allNewTypeAssignments |> Seq.tryFind(fun (tm, ts) -> ts.Name.Value = ref.tasName.Value && ref.modName.Value = tm.Name.Value) with
            | None          -> 
                let oldType = Asn1AcnAstUtilFunctions.GetActualTypeByName r ref.modName ref.tasName
                mapType r deps l lm m (oldType, us)
            | Some (mt, newTas)   -> 
                let (newKind, newPrms),ns = createReferenceType r deps l lm m None vas.Type ref (newTas.Type, us) 
                createType r None vas.Type ((newKind, newPrms),us)
                //newTas.Type, us
        | _     ->  mapType r deps l lm m (vas.Type, us)
    {
        ValueAssignment.Name = vas.Name
        c_name = vas.c_name
        ada_name = vas.ada_name
        Type = newType
        Value = mapValue vas.Value
    },ns

let private mapModule (r:Asn1AcnAst.AstRoot) (deps:Asn1AcnAst.AcnInsertedFieldDependencies) (l:ProgrammingLanguage) (lm:LanguageMacros)  (m:Asn1AcnAst.Asn1Module) (us:State) =
    let newTases, ns1 = TL "mapTas" (fun () -> m.TypeAssignments |> foldMap (fun ns nt -> mapTas r deps l lm m nt ns) us)
    //let newTases = m.TypeAssignments |> List.toArray |> Microsoft.FSharp.Collections.Array.Parallel.map (fun nt -> mapTas r deps l m nt us) |> Array.toList |> List.unzip |> fst
    //let ns1 = us

    {
        Asn1Module.Name = m.Name
        TypeAssignments = newTases
        ValueAssignments = []
        Imports = m.Imports
        Exports = m.Exports
        Comments = m.Comments
    }, ns1

let private reMapModule (r:Asn1AcnAst.AstRoot) (files0:Asn1File list) (deps:Asn1AcnAst.AcnInsertedFieldDependencies) (l:ProgrammingLanguage) (lm:LanguageMacros) (m:Asn1Module) (us:State) =
    let allNewTasses = files0 |> List.collect(fun f -> f.Modules) |> List.collect(fun m -> m.TypeAssignments |> List.map(fun ts -> (m,ts)))
    let oldModule = r.Files |> List.collect(fun f -> f.Modules) |> List.find(fun oldM -> oldM.Name.Value = m.Name.Value)
    let newVases, ns1 = oldModule.ValueAssignments |> foldMap (fun ns nt -> mapVas r allNewTasses deps l lm oldModule nt ns) us
    { m with ValueAssignments = newVases}, ns1

let private mapFile (r:Asn1AcnAst.AstRoot) (deps:Asn1AcnAst.AcnInsertedFieldDependencies) (l:ProgrammingLanguage) (lm:LanguageMacros) (f:Asn1AcnAst.Asn1File) (us:State) =
    let newModules, ns = TL "mapModules" (fun () -> f.Modules |> foldMap (fun cs m -> mapModule r deps l lm m cs) us)
    {
        Asn1File.FileName = f.FileName
        Tokens = f.Tokens
        Modules = newModules
    }, ns

let private reMapFile (r:Asn1AcnAst.AstRoot) (files0:Asn1File list) (deps:Asn1AcnAst.AcnInsertedFieldDependencies) (l:ProgrammingLanguage) (lm:LanguageMacros) (f:Asn1File) (us:State) =
    let newModules, ns = f.Modules |> foldMap (fun cs m -> reMapModule r files0 deps l lm m cs) us
    {f with Modules = newModules}, ns

let DoWork (r:Asn1AcnAst.AstRoot) (deps:Asn1AcnAst.AcnInsertedFieldDependencies) (lang:CommonTypes.ProgrammingLanguage) (lm:LanguageMacros) (encodings: CommonTypes.Asn1Encoding list) : AstRoot=
    let l = lang
//        match lang with
//        | CommonTypes.ProgrammingLanguage.C     -> DAst.ProgrammingLanguage.C
//        | CommonTypes.ProgrammingLanguage.Ada   
//        | CommonTypes.ProgrammingLanguage.Spark -> DAst.ProgrammingLanguage.Ada
//        | _                             -> raise(System.Exception "Unsupported programming language")

    let typeIdsSet = 
        r.Files |> 
        Seq.collect(fun f -> f.Modules) |> 
        Seq.collect(fun m -> m.TypeAssignments) |> 
        Seq.collect(fun tas -> mapTypeId r tas.Type) |>
        Seq.map(fun tid -> ToC (tid.AcnAbsPath.Tail.StrJoin("_").Replace("#","elem"))) |>
        Seq.groupBy id |>
        Seq.map(fun (id, lst) -> (id, Seq.length lst)) |>
        Map.ofSeq
    let initialState = {currErrorCode = 1; curErrCodeNames = Set.empty; (*allocatedTypeDefNames = []; allocatedTypeDefNameInTas = Map.empty;*) alphaIndex=0; alphaFuncs=[];typeIdsSet=typeIdsSet}
    //first map all type assignments and then value assignments
    let files0, ns = TL "mapFile" (fun () -> r.Files |> foldMap (fun cs f -> mapFile r deps l lm f cs) initialState)
    let files, ns = TL "reMapFile" (fun () -> files0 |> foldMap (fun cs f -> reMapFile r files0 deps l lm f cs) ns)
    {
        AstRoot.Files = files
        acnConstants = r.acnConstants
        args = r.args
        programUnits = DAstProgramUnit.createProgramUnits r.args files l
        lang = l
        acnParseResults = r.acnParseResults
        deps    = deps
    }

