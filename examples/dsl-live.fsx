﻿// Build the Debug 'TensorFlow.FSharp.Tests' before using this

#I __SOURCE_DIRECTORY__
#r "netstandard"
#r "../tests/bin/Debug/net461/TensorFlow.FSharp.Proto.dll"
#r "../tests/bin/Debug/net461/TensorFlow.FSharp.dll"
#r "FSharp.Compiler.Interactive.Settings"
#nowarn "49"

open System
open TensorFlow.FSharp
open TensorFlow.FSharp.DSL
open Microsoft.FSharp.Compiler.Interactive.Settings

if not System.Environment.Is64BitProcess then System.Environment.Exit(-1)

fsi.AddPrintTransformer(DT.PrintTransform)

module FirstLiveCheck = 
    let f x shift = DT.Sum(x * x, [| 1 |]) + v shift

    [<LiveCheck>]
    let check1() = f (DT.Dummy (Shape [ Dim.Named "Size1" 10;  Dim.Named "Size2" 100 ])) 3.0
     
module PlayWithFM = 
    
    let f x = 
       x * x + v 4.0 * x 

    // Get the derivative of the function. This computes "x*2 + 4.0"
    let df x = DT.diff f x  

    // Run the function
    f (v 3.0) 

    // Most operators need explicit evaluation even in interactive mode
    df (v 3.0) 

    // returns 6.0 + 4.0 = 10.0
    df (v 3.0) |> DT.Eval

    v 2.0 + v 1.0 

    // You can wrap in "fm { return ... }" if you like
    fm { return v 2.0 + v 1.0 }
      
    let test1() = 
        vec [1.0; 2.0] + vec [1.0; 2.0]
    
    // Math-multiplying matrices of the wrong sizes gives an error
    let test2() = 
        let matrix1 = 
            matrix [ [1.0; 2.0]
                     [1.0; 2.0]
                     [1.0; 2.0] ]
        let matrix2 =  
            matrix [ [1.0; 2.0]
                     [1.0; 2.0] ] 
        matrix1 *! matrix2 
    
    // Things are only checked when there is a [<LiveCheck>] exercising the code path
    
    [<LiveCheck>]
    let check1() = test1() 

    [<LiveCheck>]
    let check2() = test2() 

module GradientDescent =

    // Note, the rate in this example is constant. Many practical optimizers use variable
    // update (rate) - often reducing - which makes them more robust to poor convergence.
    let rate = 0.005

    // Gradient descent
    let step f xs =   
        // Evaluate to output values 
        xs - v rate * DT.diff f xs
    
    let train f initial numSteps = 
        initial |> Seq.unfold (fun pos -> Some (pos, step f pos  |> DT.Eval)) |> Seq.truncate numSteps 

module GradientDescentExample =

    // A numeric function of two parameters, returning a scalar, see
    // https://en.wikipedia.org/wiki/Gradient_descent
    let f (xs: Vector) : Scalar = 
        sin (v 0.5 * sqr xs.[0] - v 0.25 * sqr xs.[1] + v 3.0) * -cos (v 2.0 * xs.[0] + v 1.0 - exp xs.[1])

    // Pass this Define a numeric function of two parameters, returning a scalar
    let train numSteps = GradientDescent.train f (vec [ -0.3; 0.3 ]) numSteps

    [<LiveCheck>] 
    let check1() = train 4 |> Seq.last 
    
    let results = train 200 |> Seq.last

module ModelExample =

    let modelSize = 10

    let trainSize = 500

    let validationSize = 100

    let rnd = Random()

    let noise eps = (rnd.NextDouble() - 0.5) * eps 

    /// The true function we use to generate the training data (also a linear model plus some noise)
    let trueCoeffs = [| for i in 1 .. modelSize -> double i |]

    let trueFunction (xs: double[]) = 
        Array.sum [| for i in 0 .. modelSize - 1 -> trueCoeffs.[i] * xs.[i]  |] + noise 0.5

    let makeData size = 
        [| for i in 1 .. size -> 
            let xs = [| for i in 0 .. modelSize - 1 -> rnd.NextDouble() |]
            xs, trueFunction xs |]
         
    let prepare data = 
        let xs, y = Array.unzip data
        let xs = batchOfVecs xs
        let y = batchOfScalars y
        (xs, y)

    /// Make the training data
    let trainData = makeData trainSize |> prepare

    /// Make the validation data
    let validationData = makeData validationSize |> prepare
 
    /// Evaluate the model for input and coefficients
    let model (xs: Vectors, coeffs: Scalars) = 
        DT.Sum (xs * coeffs,axis= [| 1 |])
           
    let meanSquareError (z: DT<double>) tgt = 
        let dz = z - tgt 
        DT.Sum (dz * dz) / v (double modelSize) / v (double z.Shape.[0].Value) 

    /// The loss function for the model w.r.t. a true output
    let loss (xs, y) coeffs = 
        let y2 = model (xs, batchExtend coeffs)
        meanSquareError y y2
          
    let validation coeffs = 
        let z = loss validationData (vec coeffs)
        z |> DT.Eval

    let train initialCoeffs (xs, y) steps =
        GradientDescent.train (loss (xs, y)) initialCoeffs steps
           
    [<LiveCheck>]
    let check1() = 
        let DataSz = Dim.Named "DataSz" 100
        let ModelSz = Dim.Named "ModelSz" 10
        let coeffs = DT.Dummy (Shape [ ModelSz ])
        let xs = DT.Dummy (Shape [ DataSz; ModelSz ])
        let y = DT.Dummy (Shape [ DataSz ])
        train coeffs (xs, y) 1  |> Seq.last

    let initialCoeffs = vec [ for i in 0 .. modelSize - 1 -> rnd.NextDouble()  * double modelSize ]
    let learnedCoeffs = train initialCoeffs trainData 200 |> Seq.last |> DT.toArray
         // [|1.017181246; 2.039034327; 2.968580146; 3.99544071; 4.935430581;
         //   5.988228378; 7.030374908; 8.013975714; 9.020138699; 9.98575733|]

    validation trueCoeffs

    validation learnedCoeffs

module ODEs = 
    let lotka_volterra(du, u: DT<double>, p: DT<double>, t) = 
        let x, y = u.[0], u.[1]
        let α, β, δ, γ = p.[0], p.[1], p.[2], p.[3]
        let dx = α*x - β*x*y
        let dy = -δ*y + γ*x*y 
        DT.Pack [dx; dy]

    let u0 = vec [1.0; 1.0]
    let tspan = (0.0, 10.0)
    let p = vec [ 1.5; 1.0; 3.0; 1.0]
    //let prob = ODEProblem(lotka_volterra, u0, tspan, p)
    //let sol = solve(prob, Tsit5())
    //sol |> Chart.Lines 

module GradientDescentC =
    // mucking about with model preparation/compilation

    // Note, the rate in this example is constant. Many practical optimizers use variable
    // update (rate) - often reducing - which makes them more robust to poor convergence.
    let rate = 0.005

    // Gradient descent
    let step f xs =   
        // Evaluate to output values 
        xs - v rate * DT.diff f xs
    
    let stepC f inputShape = DT.Preprocess (step f, inputShape)

    let trainC f inputShape = 
        let stepC = stepC f inputShape
        fun initial numSteps -> initial |> Seq.unfold (fun pos -> Some (pos, stepC pos)) |> Seq.truncate numSteps 

module GradientDescentExampleC =

    // A numeric function of two parameters, returning a scalar, see
    // https://en.wikipedia.org/wiki/Gradient_descent
    let f (xs: Vector) : Scalar = 
        sin (v 0.5 * sqr xs.[0] - v 0.25 * sqr xs.[1] + v 3.0) * -cos (v 2.0 * xs.[0] + v 1.0 - exp xs.[1])

    // Pass this Define a numeric function of two parameters, returning a scalar
    let trainC = GradientDescentC.trainC f (shape [ 2 ])

    let train numSteps = trainC (vec [ -0.3; 0.3 ]) numSteps

    [<LiveCheck>] 
    let check1() = train 4 |> Seq.last 
    
    let results = train 200 |> Seq.last

module NeuralTransferFragments =

    let instance_norm (input, name) =
        use __ = DT.WithScope(name + "/instance_norm")
        let mu, sigma_sq = DT.Moments (input, axes=[0;1])
        let shift = DT.Variable (v 0.0, name + "/shift")
        let scale = DT.Variable (v 1.0, name + "/scale")
        let epsilon = v 0.001
        let normalized = (input - mu) / sqrt (sigma_sq + epsilon)
        normalized * scale + shift 

    let conv_layer (out_channels, filter_size, stride, name) input = 
        let filters = variable (DT.TruncatedNormal() * v 0.1) (name + "/weights")
        let x = DT.Conv2D (input, filters, out_channels, filter_size=filter_size, stride=stride)
        instance_norm (x, name)

    let conv_transpose_layer (out_channels, filter_size, stride, name) input =
        let filters = variable (DT.TruncatedNormal() * v 0.1) (name + "/weights")
        let x = DT.Conv2DBackpropInput(filters, input, out_channels, filter_size=filter_size, stride=stride, padding="SAME")
        instance_norm (x, name)

    let to_pixel_value (input: DT<double>) = 
        tanh input * v 150.0 + (v 255.0 / v 2.0) 

    let residual_block (filter_size, name) input = 
        let tmp = conv_layer (128, filter_size, 1, name + "_c1") input  |> relu
        let tmp2 = conv_layer (128, filter_size, 1, name + "_c2") tmp
        input + tmp2 

    let clip min max x = 
       DT.ClipByValue (x, v min, v max)

    // The style-transfer neural network
    let style_transfer input = 
        input 
        |> conv_layer (32, 9, 1, "conv1") |> relu
        |> conv_layer (64, 3, 2, "conv2") |> relu
        |> conv_layer (128, 3, 2, "conv3") |> relu
        |> residual_block (3, "resid1")
        |> residual_block (3, "resid2")
        |> residual_block (3, "resid3")
        |> residual_block (3, "resid4")
        |> residual_block (3, "resid5")
        |> conv_transpose_layer (64, 3, 2, "conv_t1") |> relu
        |> conv_transpose_layer (32, 3, 2, "conv_t2") |> relu
        |> conv_layer (3, 9, 1, "conv_t3")
        |> to_pixel_value
        |> clip 0.0 255.0
        |> DT.Eval 

    [<LiveCheck>]
    let check1() = 
        let dummyImages = DT.Dummy (Shape [ Dim.Named "BatchSz" 10; Dim.Named "H" 474;  Dim.Named "W" 712; Dim.Named "Channels" 3 ])
        style_transfer dummyImages

    