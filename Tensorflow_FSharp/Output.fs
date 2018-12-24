namespace Tensorflow

open System
open System.Runtime.InteropServices
open System.Text
open System.Globalization
open System.Linq
open Utils
open Common
open Microsoft.FSharp.NativeInterop
open System.Numerics;
open System.Collections.Generic;
open System.Linq.Expressions;

// We use this TF_Xxx as the native "TF_Xxx *" as those are opaque
type TF_Status = System.IntPtr
type TF_SessionOptions = System.IntPtr
type TF_Graph = System.IntPtr
type TF_OperationDescription = System.IntPtr
type TF_Operation = System.IntPtr
type TF_Session = System.IntPtr
type TF_DeprecatedSession = System.IntPtr
type TF_Tensor = System.IntPtr
type TF_ImportGraphDefOptions = System.IntPtr
type TF_Library = System.IntPtr
type TF_BufferPtr = System.IntPtr
type TF_Function = System.IntPtr
type TF_DeviceList = System.IntPtr

type size_t = System.UIntPtr

[<StructLayout (LayoutKind.Sequential)>]
[<Struct>]
type TF_Input = {
    operation : TF_Operation
    index : int
}

[<StructLayout (LayoutKind.Sequential)>]
[<Struct>]
type TF_Output = {
    handle : TF_Operation 
    index  : int
}

/// <summary>
/// Represents a specific input of an operation.
/// </summary>
type Input(handle : TF_Operation, index : int) =

    //extern TF_Output TF_OperationInput (TF_Input oper_in);
    [<DllImport (NativeBinding.TensorFlowLibrary)>]
    static extern TF_Output TF_OperationInput (TF_Input oper_in);

    //extern TF_DataType TF_OperationInputType (TF_Input oper_in);
    [<DllImport (NativeBinding.TensorFlowLibrary)>]
    static extern DType TF_OperationInputType (TF_Input oper_in);


    /// <summary>
    /// Initializes a new Output instance from a TF_Output struct
    /// </summary>
    /// <param name="value">The TF_Output struct</param>
    new (value:TF_Input) = new Input(value.operation, value.index)

    /// <summary>
    /// The operation that this input is for
    /// </summary>
    member this.Operation = handle

    /// <summary>
    /// The index of the output within the Operation
    /// </summary>
    member this.Index = index

    /// TODO - I'm not sure why this is needed
    member this.GetOutput (operIn : TF_Input) : Output = 
        let tfOut = TF_OperationInput operIn
        new Output(tfOut.handle,tfOut.index)

    member this.DType : DType = TF_OperationInputType ({operation = handle; index = index})

    member internal this.Struct with get () : TF_Input = { operation = handle; index = index }

/// <summary>
/// Represents a specific output of an operation on a tensor.
/// </summary>
/// <remarks>
/// <para>
/// TFOutput objects represent one of the outputs of an operation in the graph
/// (TFGraph).  Outputs have a data type, and eventually a shape that you can 
/// retrieve by calling the <see cref="M:TensorFlow.TFGraph.GetShape"/> method.
/// </para>
/// <para>
/// These can be passed as an input argument to a function for adding operations 
/// to a graph, or to the TFSession's Run and GetRunner method as values to be
/// fetched.
/// </para>
/// </remarks>
and Output(handle: IntPtr, ?index : int) =
    // extern int TF_OperationOutputNumConsumers (TF_Output oper_out);
    [<DllImport (NativeBinding.TensorFlowLibrary)>]
    static extern int TF_OperationOutputNumConsumers (Output oper_out);

    // extern TF_DataType TF_OperationOutputType (TF_Output oper_out);
    [<DllImport (NativeBinding.TensorFlowLibrary)>]
    static extern DType TF_OperationOutputType (TF_Output oper_out);

    // extern int TF_OperationOutputConsumers (TF_Output oper_out, TF_Input *consumers, int max_consumers);
    [<DllImport (NativeBinding.TensorFlowLibrary)>]
    static extern int TF_OperationOutputConsumers (TF_Output oper_out, TF_Input* consumers, int max_consumers);

    member internal this.Struct with get () = { handle = handle; index = index |> Option.orDefault 0}

    //do if handle = null then raise(ArgumentNullException ("Outputs does not have a valid operation pointer"))
    member this.LLOperation = handle

    /// <summary>
    /// The index of the output within the operation.
    /// </summary>
    member this.Index = index.Value

    /// <summary>
    /// Gets the number consumers.
    /// </summary>
    /// <value>The number consumers.</value>
    /// <remarks>
    /// This number can change when new operations are added to the graph.
    /// </remarks>
    member this.NumConsumers = TF_OperationOutputNumConsumers (this);

    /// <summary>
    /// Gets the type of the output.
    /// </summary>
    /// <value>The type of the output.</value>
    member this.DType = if this.LLOperation = IntPtr.Zero then DType.Unknown else TF_OperationOutputType (this.Struct)
    //public TFDataType OutputType => LLOperation == IntPtr.Zero ? TFDataType.Unknown : TF_OperationOutputType (this);

    /// <summary>
    /// Initializes a new Output instance.
    /// </summary>
    /// <param name="operation">The operation to which to attach the output.</param>
    /// <param name="index">The index of the output within the operation, if not specified, it defaults to zero.</param>
    new (operation : Operation, ?index : int) =
        if box operation = null then raise(ArgumentNullException ("operation"))
        Output(operation.Handle, ?index=index)

    /// <summary>
    /// Initializes a new Output instance from another Output
    /// </summary>
    /// <param name="output">The other Output that is having its operation attached.</param>
    /// <param name="index">The index of the output within the operation, if not specified, it defaults to zero.</param>
    new (output : Output, ?index : int) = 
        if box output.LLOperation = null then raise(ArgumentNullException ("Outputs does not have a valid operation pointer"))
        Output(output.LLOperation, ?index=index)

    /// <summary>
    /// Initializes a new Output instance from a TF_Output struct
    /// </summary>
    /// <param name="value">The TF_Output struct</param>
    new (value:TF_Output) = new Output(value.handle, value.index)

    /// <summary>
    /// Get list of all current consumers of a specific output of an operation
    /// </summary>	
    /// <value>The output consumers.</value>
    /// <remarks>
    /// A concurrent modification of the graph can increase the number of consumers of
    /// an operation.
    /// This can return null if the Output does not point to a valid object.
    /// </remarks>
    member this.OutputConsumers 
        with get() : Input[] = 
            let result = Array.zeroCreate<TF_Input> this.NumConsumers
            use first = fixed &result.[0]
            TF_OperationOutputConsumers (this.Struct, first, result.Length) |> ignore
            result |> Array.map (fun x -> Input(x.operation,x.index))
            

    /// <summary>
    /// The associated operation.
    /// </summary>
    /// <value>The operation.</value>
    member this.Operation = new Operation (this.LLOperation)

    /// <summary>
    /// Returns a <see cref="T:System.String"/> that represents the current <see cref="T:TensorFlow.Output"/>.
    /// </summary>
    /// <returns>A <see cref="T:System.String"/> that represents the current <see cref="T:TensorFlow.Output"/>.</returns>
    override this.ToString () =
        sprintf "[%O Index=%i Operation=%O (0x%i)]"  this.DType this.Index this.Operation this.LLOperation 

    interface IComparable
        with 
            member this.CompareTo(x : obj) = 
                if (x.GetType() <> this.GetType()) then -1
                else (this :> IComparable<Output>).CompareTo(x :?> Output);

    interface IComparable<Output>
        with 
            member this.CompareTo(other : Output) =
                let left = this.Operation.Handle.ToInt64();
                let right = other.Operation.Handle.ToInt64();
                if left <> right then
                    left.CompareTo(right);
                else this.Index.CompareTo(other.Index);
