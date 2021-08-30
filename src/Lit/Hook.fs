namespace Lit

open System
open Fable.Core
open Fable.Core.JsInterop

[<RequireQualifiedAccess>]
type internal Effect =
    | OnConnected of (unit -> IDisposable)
    | OnRender of (unit -> unit)

[<AttachMembers>]
type HookDirective() =
    inherit AsyncDirective()

    let mutable _firstRun = true
    let mutable _rendering = false
    let mutable _args = [||]

    let mutable _stateIndex = 0
    let _states = ResizeArray<obj>()

    let _effects = ResizeArray<Effect>()
    let _disposables = ResizeArray<IDisposable>()

    member _.renderFn = Unchecked.defaultof<JS.Function>

    // TODO: Improve error message for each situation
    member _.fail() =
        failwith "Hooks must be called consistently for each render call"

    member this.createTemplate() =
        _stateIndex <- 0
        _rendering <- true
        let res = this.renderFn.apply(this, _args)
        // TODO: Do same check for effects?
        if not _firstRun && _stateIndex <> _states.Count then
            this.fail()
        _rendering <- false
        if this.isConnected then
            this.runEffects(onRender=true, onConnected=_firstRun)
        _firstRun <- false
        res

    member this.checkRendering() =
        if not _rendering then
            this.fail()

    member _.runEffects(onConnected: bool, onRender: bool) =
        // lit-html doesn't provide a didUpdate callback so just use a 0 timeout.
        JS.setTimeout (fun () ->
            _effects |> Seq.iter (function
                | Effect.OnRender effect ->
                    if onRender then effect()
                | Effect.OnConnected effect ->
                    if onConnected then
                        _disposables.Add(effect()))) 0 |> ignore

    member this.render([<ParamArray>] args: obj[]) =
        _args <- args
        this.createTemplate()

    member this.setState(index: int, value: 'T): unit =
        _states.[index] <- value
        if not _rendering then
            this.createTemplate() |> this.setValue

    member this.getState(): 'T * int =
        if _stateIndex >= _states.Count then
            this.fail()
        let idx = _stateIndex
        _stateIndex <- idx + 1
        _states.[idx] :?> _, idx

    member this.useState(init: unit -> 'T): 'T * ('T -> unit) =
        this.checkRendering()
        let state, index =
            if _firstRun then
                let state = init()
                _states.Add(state)
                state, _states.Count - 1
            else
                this.getState()

        state, fun v -> this.setState(index, v)

    member this.useRef<'T>(init: unit -> 'T): RefValue<'T> =
        this.checkRendering()
        if _firstRun then
            let ref = Lit.createRef<'T>()
            ref.value <- init()
            _states.Add(box ref)
            ref
        else
            this.getState() |> fst

    member _.useEffect(effect): unit =
        if _firstRun then
            _effects.Add(Effect.OnRender effect)

    member _.useEffectOnce(effect): unit =
        if _firstRun then
            _effects.Add(Effect.OnConnected effect)

    member _.disconnected() =
        for disp in _disposables do
            disp.Dispose()
        _disposables.Clear()

    // In some situations, a disconnected part may be reconnected again,
    // so we need to re-run the effects but the old state is kept
    // https://lit.dev/docs/api/custom-directives/#AsyncDirective
    member this.reconnected() =
        this.runEffects(onConnected=true, onRender=false)

type HookComponentAttribute() =
    inherit JS.DecoratorAttribute()
    override _.Decorate(fn) =
        emitJsExpr (jsConstructor<HookDirective>, fn)
            "class extends $0 { renderFn = $1 }"
        |> LitHtml.directive :?> _

type Hook() =
    static member inline useState(v: 'Value) =
        jsThis<HookDirective>.useState(fun () -> v)

    static member inline useState(init: unit -> 'Value) =
        jsThis<HookDirective>.useState(init)

    static member inline useRef<'Value>(): RefValue<'Value option> =
        jsThis<HookDirective>.useRef<'Value option>(fun () -> None)

    static member inline useRef(v: 'Value): RefValue<'Value> =
        jsThis<HookDirective>.useRef(fun () -> v)

    static member inline useMemo(init: unit -> 'Value): 'Value =
        jsThis<HookDirective>.useRef(init).value

    // TODO: Dependencies?
    static member inline useEffect(effect: unit -> unit) =
        jsThis<HookDirective>.useEffect(effect)

    static member inline useEffectOnce(effect: unit -> unit) =
        jsThis<HookDirective>.useEffectOnce(fun () -> effect(); Hook.emptyDisposable)

    static member inline useEffectOnce(effect: unit -> IDisposable) =
        jsThis<HookDirective>.useEffectOnce(effect)

    static member createDisposable(f: unit -> unit) =
        { new IDisposable with
            member _.Dispose() = f() }

    static member emptyDisposable =
        { new IDisposable with
            member _.Dispose() = () }

    static member inline useCancellationToken () =
        Hook.useCancellationToken(jsThis)

    static member useCancellationToken (this: HookDirective) =
        let cts = this.useRef(fun () -> new Threading.CancellationTokenSource())
        let token = this.useRef(fun () -> cts.value.Token)

        this.useEffectOnce(fun () ->
            Hook.createDisposable(fun () ->
                cts.value.Cancel()
                cts.value.Dispose()
            )
        )

        token