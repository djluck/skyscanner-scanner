open FSharp.Data
open Types
open Search
open System
open SkyScannerApi

[<EntryPoint>]
let main argv = 

    let request = { 
        FlightDatesToSearchBetween = Search.GetIdealFlightDates(new DateTime(2015, 1, 20), new DateTime(2015, 2, 23)) |> List.ofSeq
        MaxFlightTime = new TimeSpan(22, 0, 0)
        MaxStops = 3
        MaxPrice = 430m
        Destinations = ["CMB";]
        //Laos =  ["VTE"; "LPG]"
        //Argentina = 
        //cambodia = ["PNH"; "REP";]
        //vietnam = ["DAD"; "HAN"; "SGN"; "DAD"; "PQC";]
        //SriLanka = CMB = Colombo
        //Indonesia = ["CGK"; "DPS"; "JOG"; "KNO"; "SUB"; "TKG";]
        //RGN = Mayanmar

    }

    //how many searches per day can we get away with?
    //1000 searches per day

    let result = Search.FindSuitableFlights request |> Async.RunSynchronously
    printfn "Ordered by cost: %A" (result |> List.sortBy (fun x -> x.Price) |> Seq.take 3)
    printfn "Ordered by total flight time: %A" (result |> List.sortBy (fun x -> x.Inbound.Duration + x.Outbound.Duration) |> Seq.take 3)
    printfn "Ordered by length of trip: %A" (result |> List.sortBy (fun x -> -(x.Inbound.DepartsAt - x.Outbound.DepartsAt).Ticks) |> Seq.take 3)
    Console.ReadLine()
        |> ignore
    0 // return an integer exit code
