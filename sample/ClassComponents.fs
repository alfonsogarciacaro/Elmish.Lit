﻿module ClassComponents

open Elmish
open Lit
open Fable.Core
open Fetch
open Fable.Core.JsInterop
open Lit.Elmish

[<ImportMember("./controllers.js")>]
type MouseController(host) =
    inherit ReactiveController(host)
    member _.x: float = jsNative
    member _.y: float = jsNative

type Post =
    {| usedId: int
       id: int
       title: string
       body: string |}

type ApiController(host: LitElement) =
    inherit ReactiveController(host)

    member val Posts: Post array = Array.empty with get, set
    member val Page: int = 1 with get, set
    member val Limit: int = 10 with get, set

    member this.FetchUsers(?page: int, ?limit: int) =
        let page = defaultArg page this.Page
        let limit = defaultArg limit this.Limit

        promise {
            let! posts =
                promise {
                    let! result = tryFetch $"https://jsonplaceholder.typicode.com/posts?_page={page}&_limit={limit}" []

                    match result with
                    | Ok res ->
                        let! result = res.json () :?> JS.Promise<Post array>
                        return result
                    | Error err -> return Array.empty<Post>
                }

            this.Posts <- posts
            host.requestUpdate ()
        }

    member this.NextPage() =
        this.Page <- this.Page + 1
        this.FetchUsers() |> Promise.start

    override this.hostConnected() : unit = this.FetchUsers() |> Promise.start

type UserProfile() =
    inherit LitElement()

    let name = ""
    let age = 0

    override _.render() = html $"<p>{name} - {age}</p>"

type SampleState = { counter: int }
type SampleMsg =
    | Increment
    | Decrement
let sampleInit () = { counter = 0 }, Cmd.none

let sampleUpdate msg state =
    match msg with
    | Increment -> { state with counter = state.counter + 1 }, Cmd.none
    | Decrement -> { state with counter = state.counter - 1 }, Cmd.none
    
type ElementWithController() =
    inherit LitElement()

    let mouse = MouseController jsThis
    let api = ApiController jsThis
    let elmish = ElmishController(jsThis, sampleInit, sampleUpdate)

    let posts (post: Post) = html $"<li>{post.title}</li>"

    override _.render() =
        html
            $"""
            <p>{mouse.x} - {mouse.y}</p>
            <p>Elmish Controller Counter: {elmish.state.counter} </p>
            <button @click={fun _ -> elmish.dispatch Increment}>Increment</button>
            <button @click={fun _ -> elmish.dispatch Decrement}>Decrement</button>
            <p>Page: {api.Page}</p>
            <button @click={fun _ -> api.NextPage()}>Next Page</button>
            <ul>{Lit.mapUnique (fun (p: Post) -> $"{p.id}") posts api.Posts}</li>
        """
