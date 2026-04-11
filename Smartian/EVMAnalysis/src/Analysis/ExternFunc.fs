module EVMAnalysis.Semantics.ExternFunc

open B2R2.BinIR
open B2R2.BinIR.LowUIR
open EVMAnalysis
open EVMAnalysis.Domain.Taint
open EVMAnalysis.Domain.AbsVal
open EVMAnalysis.Domain.State
open EVMAnalysis.Semantics.Evaluate

let rec private consToList argExp =
  match argExp with
  | Nil -> []
  | BinOp (BinOpType.CONS, _, { E = e1 }, { E = e2 }, _) -> e1 :: consToList e2
  | _ -> failwithf "Unexpected arg expr: %s" (Pp.expToString { E = argExp } )

// We are only interested SHA3 calls for computing the address of mapping or
// 'array' type storage variables.
let private runSha3 offset length state funcInfo =
  match AbsVal.tryGetConst offset, AbsVal.tryGetConst length with
  | Some off, Some len when len = 32I ->
    let idVal = State.loadMemory (AbsVal.ofBigInt off) state
    (state, funcInfo, AbsVal.sha32Byte idVal)
  | Some off, Some len when len = 64I ->
    // A variable used as a key of mapping must be considered as a use.
    let keyVal = State.loadMemory (AbsVal.ofBigInt off) state
    printfn "%s is used in the position of mapping key" (AbsVal.toString keyVal)
    let funcInfo = FuncInfo.recordUse keyVal funcInfo
    // Next, find the ID of the map that is going to be accessed.
    let idVal = State.loadMemory (AbsVal.ofBigInt (off + 32I)) state
    (state, funcInfo, AbsVal.sha64Byte idVal)
  | _ -> (state, funcInfo, AbsVal.unknown) // TODO: consider taint propagation.

let rec private storeConstrArg accState dst argNum idx =
  if idx >= argNum then accState
  else let addr = AbsVal.ofBigInt (dst + idx * 32I)
       let accState = State.storeMemory addr AbsVal.ConstrArg accState
       storeConstrArg accState dst argNum (idx + 1I)

let private runCodeCopy dstVal state funcInfo =
  let argNum = funcInfo.FuncSpec.ArgSpecs.Length - 1 // Exclude ether value arg.
  printfn "Found codecopy(%s, _, _), arg# = %d" (AbsVal.toString dstVal) argNum
  match AbsVal.tryGetConst dstVal with
  | Some dst when dst <> 0I -> // dst != 0 means constructor arg copy
    storeConstrArg state dst (bigint argNum) 0I, funcInfo, AbsVal.bot
  | _ -> (state, funcInfo, AbsVal.bot)

let private handleFunc addr funcName args state funcInfo =
  match funcName, args with
  | "sstore", [k; v] ->
    printfn "Found sstore(%s, %s) @ %s"
      (AbsVal.toString k) (AbsVal.toString v) (Addr.toString addr)
    (state, FuncInfo.recordSStore k v funcInfo, AbsVal.bot)
  | "sload", [k] ->
    printfn "Found sload(%s) @ %s" (AbsVal.toString k) (Addr.toString addr)
    let retVal = AbsVal.toVariables k |> Taint.ofVars |> AbsVal.ofTaint
    (state, funcInfo, retVal)
  | "keccak256", [offset; len] ->
    printfn "Found keccak256(...) @ %s" (Addr.toString addr)
    runSha3 offset len state funcInfo
  | "codecopy", [dst; _; _] -> runCodeCopy dst state funcInfo
  | "msg.sender", [] -> state, funcInfo, AbsVal.Caller
  | "exp", [b; e] -> state, funcInfo, AbsVal.exp b e
  | "call", [_; addrVal; amountVal; _; _; _; _]
  | "callcode", [_; addrVal; amountVal; _; _; _; _] ->
    printfn "Found call*(..., %s, %s) @ %s"
      (AbsVal.toString addrVal) (AbsVal.toString amountVal) (Addr.toString addr)
    let funcInfo = FuncInfo.recordUse addrVal funcInfo
    let funcInfo = FuncInfo.recordUse amountVal funcInfo
    (state, funcInfo, AbsVal.unknown)
  | "selfdestruct", [addrVal] ->
    printfn "Found selfdestruct(%s) @ %s"
      (AbsVal.toString addrVal) (Addr.toString addr)
    (state, FuncInfo.recordUse addrVal funcInfo, AbsVal.unknown)
  // Add more semantics if needed.
  | _ -> (state, funcInfo, AbsVal.unknown)

let runExtern addr funcExp argExp state funcInfo =
  match funcExp with
  | FuncName fName ->
    let args = consToList argExp |> List.map (fun e -> eval e state)
    handleFunc addr fName args state funcInfo
  | _ -> failwithf "Unexpected func expr: (%s)" (Pp.expToString { E = funcExp })
