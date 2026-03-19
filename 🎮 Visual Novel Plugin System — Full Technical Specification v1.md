# **🎮 Visual Novel Plugin System — Full Technical Specification v1**

---

# **⭐ System Overview**

The VN system is a **deterministic async command graph interpreter**.

It executes scripts authored in JSON, compiled into binary Godot Resources, and interpreted fully in memory at runtime.

The system has **three layers**:

Authoring Layer → Storage Layer → Runtime Layer

---

# **🧾 AUTHORING LAYER (LOCKED)**

## **Authoring Format**

* VN scripts MUST be authored as JSON files.

* JSON files MUST be human-readable.

* JSON files MUST be stored inside the project repository.

No database usage.

No direct `.tres` editing.

No runtime JSON parsing.

---

## **Directory Structure**

res://vn/

  scripts\_json/

     \*.json

  compiled/

     \*.res

Rules:

* JSON scripts stored only in `scripts_json`

* Compiled resources stored only in `compiled`

* File name must match script\_id

---

## **Root JSON Schema**

{

 "script\_id": "string",

 "start": "string",

 "commands": \[ CommandObject \]

}

Rules:

* script\_id REQUIRED and unique

* start REQUIRED and must reference valid command id

* commands REQUIRED and must be flat array

* No extra root keys allowed

---

## **Command JSON Schema**

{

 "id": "string",

 "type": "string",

 "next": "string or null",

 "data": { },

 "branches": { }

}

Rules:

* id REQUIRED unique

* type REQUIRED

* data REQUIRED (can be empty)

* branches REQUIRED (can be empty)

* next REQUIRED except Choice / Jump / ConditionalJump / Include

* Unknown keys forbidden

---

## **Namespaced ID Rule**

Compiler MUST convert command ids to:

scriptId.commandId

Example:

intro.start

anna.route\_01

shared.flashback\_03

---

# **📦 COMMAND PAYLOAD CONTRACTS (LOCKED)**

### **ShowText (blocking)**

data:

{

 "text\_key": "string",

 "fallback\_text": "string optional",

 "speaker": "string optional",

 "portrait\_id": "string optional",

 "portrait\_side": "left|right optional"

}

---

### **Choice (blocking)**

data:

{

 "options": \[

   {

     "text\_key": "string",

     "next": "string",

     "condition": "string optional",

     "set": { "varName": "operationValue optional" }

   }

 \]

}

Rules:

* options length ≥ 1

* condition evaluated at runtime

* variable mutations applied before jump

---

### **Jump (non-blocking)**

branches:

{

 "target": "string"

}

---

### **ConditionalJump**

data:

{

 "condition": "string"

}

branches:

{

 "true": "string",

 "false": "string"

}

---

### **Include (blocking)**

data:

{

 "target\_script": "string",

 "entry\_id": "string"

}

next:

"return\_id"

Runtime behavior:

* push return\_id stack

* jump to entry\_id

* when included script ends → pop stack and resume

---

### **SetVariable**

data:

{

 "name": "string",

 "operation": "set|add|sub",

 "value": "number|string|bool"

}

---

### **Delay (blocking)**

data:

{

 "seconds": number \> 0

}

---

### **ShowImage (non-blocking)**

data:

{

 "element\_id": "string",

 "texture": "string",

 "x\_percent": number 0..1,

 "y\_percent": number 0..1,

 "z": integer

}

---

### **MoveImage (non-blocking)**

data:

{

 "element\_id": "string",

 "x\_percent": number,

 "y\_percent": number,

 "duration": number \>= 0

}

---

### **ClearImage (non-blocking)**

data:

{

 "element\_id": "string"

}

---

### **ClearVideo (non-blocking)**

data:

{

 "element\_id": "string"

}  
Prematurely stops the given Video playing.

---

### **PlayVoice (non-blocking)**

data:

{

 "audio": "string"

}

Voice stops when dialogue advances.

---

### **PlayBGM (non-blocking)**

data:

{

 "audio": "string",

 "fade": number optional,  
“Loop”: true optional, default false

}

---

### **PlaySFX (non-blocking)**

data:

{

 "audio": "string",

 "fade": number optional,  
“Loop”: true optional, default false

}

---

### **PlayVideo (non-blocking)**

data:

{

 "video": "string",  
“Loop”: false optional, default false

}

Video keeps playing until it ends, unless loop is true

---

# **🧠 CONDITION EXPRESSION GRAMMAR (LOCKED)**

Allowed operators:

\> \< \>= \<= \== \!=

AND OR

Rules:

* variables referenced by name

* string values in double quotes

* boolean true / false

* parentheses allowed

* precedence:

1 parentheses  
 2 comparison  
 3 AND  
 4 OR

Unknown variables evaluate as false.

No functions allowed.

---

# **📦 STORAGE LAYER (LOCKED)**

## **Compiler Responsibilities**

For each JSON script:

1. Scan scripts\_json directory

2. Parse JSON

3. Validate root contract

4. Validate command contracts

5. Validate unique ids

6. Apply namespace prefix

7. Validate references

8. Validate condition syntax

9. Run reachability analysis

10. Build Resource objects

11. Serialize binary `.res` file

12. Emit compile report

Compilation stops on errors.

Warnings allowed for unreachable nodes.

---

# **🎮 RUNTIME LAYER (LOCKED)**

## **Script Loading**

Runtime loads only:

res://vn/compiled/{script\_id}.res

Never reads JSON.

---

## **Runtime Graph Build**

On Play:

* Convert resource array → Dictionary\<string, VNCommandResource\>

---

## **Execution Loop**

currentId \= startId

while currentId \!= null:

  cmd \= dictionary\[currentId\]

  nextOverride \= null

  await Execute(cmd)

  currentId \= ResolveNext(cmd, nextOverride)

---

## **ResolveNext Priority**

1 choice override  
 2 conditional branch  
 3 cmd.next  
 4 include return stack  
 5 null → end

---

## **Blocking Commands**

* ShowText

* Choice

* Delay

* ConditionalJump evaluation

* PlayVideo

* Include

---

## **Non-Blocking Commands**

* ShowImage

* MoveImage

* ClearImage

* PlayVoice

* PlayBGM

* SetVariable

* Jump

---

## **State Store**

Runtime keeps:

Dictionary\<string, Variant\> variables

HashSet\<string\> visitedCommands

Stack\<string\> includeReturnStack

---

## **Viewport System**

* Runtime dictionary of element\_id → node

* Elements persist until ClearImage

* ShowImage replaces existing id

* Coordinates calculated using safe area percentage

---

## **Save Snapshot**

Contains:

currentId

variables

visitedCommands

viewport element states

bgm state

includeReturnStack

Load procedure:

1 restore variables  
 2 rebuild viewport  
 3 restore bgm  
 4 resume runner

---

# **🧭 FULL IMPLEMENTATION ROADMAP (ALL LAYERS)**

### **Phase A — Authoring Contracts**

1 define JSON schema model classes  
 2 define condition grammar validator

---

### **Phase B — Storage Compiler**

3 directory scanner  
 4 JSON loader  
 5 root validator  
 6 command validator  
 7 id namespace resolver  
 8 reference validator  
 9 reachability analyzer  
 10 resource builder  
 11 binary serializer  
 12 compile report

Checkpoint: JSON → .res works.

---

### **Phase C — Runtime Core**

13 script dictionary builder  
 14 VNStateStore  
 15 async VNRunner loop

Checkpoint: Jump \+ Delay script playable.

---

### **Phase D — UI Systems**

16 DialogueBox  
 17 ChoiceUI  
 18 ViewportLayer  
 19 AudioManager

Checkpoint: full dialogue \+ branching playable.

---

### **Phase E — Persistence**

20 Save snapshot builder  
 21 Load reconstruction

Checkpoint: mid-VN save works.

---

### **Phase F — Debug Tools**

22 compile validator window  
 23 runtime step inspector  
 24 variable viewer

Checkpoint: designers can debug scripts.

---

# **✅ FINAL STATE**

This specification defines:

* exact authoring format

* exact storage pipeline

* exact runtime behavior

* exact command contracts

* exact condition grammar

* exact modularization mechanism

* exact save semantics

* exact execution order

