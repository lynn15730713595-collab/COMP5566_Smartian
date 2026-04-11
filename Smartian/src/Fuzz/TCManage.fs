module Smartian.TCManage

open Nethermind.Evm
open Executor
open Options
open Utils

(*** Directory paths ***)

let mutable tcDir = ""
let mutable bugDir = ""

(*** Types, variables, and functions for target bug specification ***)

type TargetSite = ProgramCounter of int | Function of string

let mutable earlyTerminateFlag = false
let mutable targetBugs = Set.empty

// Register the bug as our target bug. Depending on 'bugType', we must interpret
// the input string differently.
let addTargetBug (bugType, siteStr) =
  let bugSite =
    match bugType with
    | BugClass.Reentrancy
    | BugClass.EtherLeak
    | BugClass.SuicidalContract -> Function siteStr
    | _ -> ProgramCounter (System.Convert.ToInt32(siteStr, 16))
  targetBugs <- Set.add (bugType, bugSite) targetBugs

// Check if the found bug is one of our target bugs, and update the target bug
// set if so. Depending on 'bugType', we must choose between 'pc' and 'func'.
let removeTargetBug (bugType, pc, func, _) =
  let bugSite =
    match bugType with
    | BugClass.Reentrancy
    | BugClass.EtherLeak
    | BugClass.SuicidalContract -> Function func
    | _ -> ProgramCounter pc
  targetBugs <- Set.remove (bugType, bugSite) targetBugs

let initialize outDir targetBugs =
  tcDir <- System.IO.Path.Combine(outDir, "testcase")
  System.IO.Directory.CreateDirectory(tcDir) |> ignore
  bugDir <- System.IO.Path.Combine(outDir, "bug")
  System.IO.Directory.CreateDirectory(bugDir) |> ignore
  if not (Array.isEmpty targetBugs) then earlyTerminateFlag <- true
  Array.iter addTargetBug targetBugs

(*** Statistics ***)

let mutable private totalTC = 0
let mutable private totalBug = 0
let mutable private totalAF = 0
let mutable private totalAW = 0
let mutable private totalBD = 0
let mutable private totalCH = 0
let mutable private totalEL = 0
let mutable private totalIB = 0
let mutable private totalME = 0
let mutable private totalMS = 0
let mutable private totalRE = 0
let mutable private totalSC = 0
let mutable private totalTO = 0
let mutable private totalFE = 0
let mutable private totalRV = 0

let checkFreezingEtherBug () =
  if receivedEther && useDelegateCall && not canSendEther then
    totalFE <- totalFE + 1

let printStatistics () =
  log "Total Executions: %d" totalExecutions
  log "Deployment failures: %d" deployFailCount
  log "Test Cases: %d" totalTC
  log "Covered Edges: %d" accumRuntimeEdges.Count
  log "Covered Instructions: %d" accumRuntimeInstrs.Count
  log "Covered Def-Use Chains: %d" accumDUChains.Count
  log "Found Bugs:"
  log "  Assertion Failure: %d" totalAF
  log "  Arbitrary Write: %d" totalAW
  log "  Block state Dependency: %d" totalBD
  log "  Control Hijack: %d" totalCH
  log "  Ether Leak: %d" totalEL
  log "  Integer Bug: %d" totalIB
  log "  Mishandled Exception: %d" totalME
  log "  Multiple Send: %d" totalMS
  log "  Reentrancy: %d" totalRE
  log "  Suicidal Contract: %d" totalSC
  log "  Transaction Origin Use: %d" totalTO
  log "  Freezing Ether: %d" totalFE
  log "  Requirement Violation: %d" totalRV

let getTestCaseCount () =
  totalTC

(*** Record of paths and bugs ***)

let private updateBugCountAux (bugClass, _, _, _) =
  match bugClass with
  | BugClass.AssertionFailure -> totalAF <- totalAF + 1
  | BugClass.ArbitraryWrite -> totalAW <- totalAW + 1
  | BugClass.BlockstateDependency -> totalBD <- totalBD + 1
  | BugClass.ControlHijack -> totalCH <- totalCH + 1
  | BugClass.EtherLeak -> totalEL <- totalEL + 1
  | BugClass.IntegerBug -> totalIB <- totalIB + 1
  | BugClass.MishandledException -> totalME <- totalME + 1
  | BugClass.MultipleSend -> totalMS <- totalMS + 1
  | BugClass.Reentrancy -> totalRE <- totalRE + 1
  | BugClass.SuicidalContract -> totalSC <- totalSC + 1
  | BugClass.TransactionOriginUse -> totalTO <- totalTO + 1
  | BugClass.RequirementViolation -> totalRV <- totalRV + 1
  | _ -> ()

let private updateBugCount bugSet =
  Set.iter updateBugCountAux bugSet

(*** Test case storing functions ***)

let printBugInfo bugSet =
  let iterator (bugClass, pc, funcName, txIdx) =
    let bugStr = BugClassHelper.toString bugClass
    let funcStr =
      match bugClass with
      | BugClass.Reentrancy
      | BugClass.EtherLeak
      | BugClass.SuicidalContract -> sprintf " (%s)" funcName
      | _ -> ""
    log "Tx#%d found %s at %x%s" txIdx bugStr pc funcStr
  Set.iter iterator bugSet

let private decideBugTag bugSet =
  Set.map (fun (bugType, _, _, _) -> bugType) bugSet
  |> Set.map BugClassHelper.toTag
  |> String.concat "-"

let private dumpBug opt seed bugSet =
  printBugInfo bugSet
  updateBugCount bugSet
  let tag = decideBugTag bugSet
  let tc = Seed.concretize seed
  let tcStr = TestCase.toJson tc
  let tcName = sprintf "id-%05d-%s_%05d" totalBug tag (elapsedSec())
  let tcPath = System.IO.Path.Combine(bugDir, tcName)
  if opt.Verbosity >= 0 then
    log "[*] Save bug seed %s: %s" tcName (Seed.toString seed)
  System.IO.File.WriteAllText(tcPath, tcStr)
  Set.iter removeTargetBug bugSet
  if earlyTerminateFlag && Set.isEmpty targetBugs then
    log "Found all target bugs at %d second (early termination)" (elapsedSec())
    log "===== Statistics ====="
    printStatistics ()
    exit (0)
  totalBug <- totalBug + 1

let private dumpTestCase opt seed =
  let tc = Seed.concretize seed
  let tcStr = TestCase.toJson tc
  let tcName = sprintf "id-%05d_%05d" totalTC (elapsedSec())
  let tcPath = System.IO.Path.Combine(tcDir, tcName)
  if opt.Verbosity >= 1 then
    log "[*] Save new seed %s: %s" tcName (Seed.toString seed)
  System.IO.File.WriteAllText(tcPath, tcStr)
  totalTC <- totalTC + 1

let evalAndSave opt seed =
  let covGain, duGain, bugSet = Executor.getCoverage opt seed
  if Set.count bugSet > 0 then dumpBug opt seed bugSet
  if covGain then dumpTestCase opt seed
  if not covGain && duGain && opt.Verbosity >= 2 then
    log "[*] Internal new seed: %s" (Seed.toString seed)
  covGain || duGain // Returns whether this seed is meaningful.
