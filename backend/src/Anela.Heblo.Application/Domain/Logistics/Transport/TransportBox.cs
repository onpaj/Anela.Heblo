using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using Anela.Heblo.Xcc.Domain;
using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Application.Domain.Logistics.Transport;

public class TransportBox : Entity<int>
{
    private List<TransportBoxItem> _items = new ();
    private List<TransportBoxStateLog> _stateLog = new ();

    public IReadOnlyList<TransportBoxItem> Items => _items;

    public string? Code { get; private set; }
    public TransportBoxState State { get; private set; } = TransportBoxState.New;
    public TransportBoxState DefaultReceiveState { get; private set; } = TransportBoxState.Stocked;
    public string? Description { get; set; }
    public DateTime? LastStateChanged { get; set; }
    
    public string? Location { get; set; }
    
    public IReadOnlyList<TransportBoxStateLog> StateLog => _stateLog;

    public static Expression<Func<TransportBox, bool>> IsInTransportPredicate = b => b.State == TransportBoxState.InTransit || b.State == TransportBoxState.Received || b.State == TransportBoxState.Opened;
    public static Func<TransportBox, bool> IsInTransportFunc = IsInTransportPredicate.Compile();
    public bool IsInTransit => IsInTransportFunc(this);
    
    public static Expression<Func<TransportBox, bool>> IsInReservePredicate = b => b.State == TransportBoxState.Reserve;
    public static Func<TransportBox, bool> IsInReserveFunc = IsInReservePredicate.Compile();
    public bool IsInReserve => IsInReserveFunc(this);
    
    public TransportBoxState? NextState => TransitionNode.NextState?.NewState;
    public TransportBoxState? PreviousState => TransitionNode.PreviousState?.NewState;
    public TransportBoxStateNode TransitionNode => _transitions[State];

    public void Open(string boxCode, DateTime date, string userName)
    {
        ChangeState(TransportBoxState.Opened, date, userName, TransportBoxState.New, TransportBoxState.InTransit, TransportBoxState.Reserve);
        Code = boxCode;
        Location = null;
    }

    
    public TransportBoxItem AddItem(string productCode, string productName, double amount, DateTime date, string userName)
    {
        CheckState(TransportBoxState.Opened, TransportBoxState.Opened);
        var newItem = new TransportBoxItem(productCode, productName, amount, date, userName);
        _items.Add(newItem);

        return newItem;
    }
    
    public TransportBoxItem? DeleteItem(int itemId)
    {
        CheckState(TransportBoxState.Opened, TransportBoxState.Opened);
        var toDelete = _items.SingleOrDefault(s => s.Id == itemId);
        return toDelete;
    }


    public void Reset(DateTime date, string userName)
    {
        _items.Clear();
        Code = null;
        ChangeState(TransportBoxState.New, date, userName);
    }

    public void ToTransit(DateTime date, string userName)
    {
        ChangeState(TransportBoxState.InTransit, date, userName, TransportBoxState.Opened, TransportBoxState.Error);
    }
    
    public void ToReserve(DateTime date, string userName, TransportBoxLocation location)
    {
        Location = location.ToString();
        ChangeState(TransportBoxState.Reserve, date, userName, TransportBoxState.Opened, TransportBoxState.Error);
    }

    public void Receive(DateTime date, string userName, TransportBoxState receiveState = TransportBoxState.Stocked)
    {
        DefaultReceiveState = receiveState;
        ChangeState(TransportBoxState.Received, date, userName, TransportBoxState.InTransit, TransportBoxState.Reserve);
    }
    
    
    public void ToSwap(DateTime date, string userName)
    {
        ChangeState(TransportBoxState.InSwap, date, userName, TransportBoxState.Received, TransportBoxState.Stocked);
    }
    
    public void ToPick(DateTime date, string userName)
    {
        ChangeState(TransportBoxState.Stocked, date, userName, TransportBoxState.InSwap, TransportBoxState.Received);
    }
    
    public void Close(DateTime date, string userName)
    {
        ChangeState(TransportBoxState.Closed, date, userName);
    }

    private void ChangeState(TransportBoxState newState, DateTime now, string userName, params TransportBoxState[] allowedStates)
    {
        ChangeState(newState, now, userName, null, allowedStates);
    }

    private void ChangeState(TransportBoxState newState, DateTime now, string userName, string? description, TransportBoxState[] allowedStates)
    {
        CheckState(newState, allowedStates);

        if(description != null)
            Description += Environment.NewLine + description;
        State = newState;
        LastStateChanged = now;
        _stateLog.Add(new TransportBoxStateLog(newState, now, userName, description));
    }

    private void CheckState(TransportBoxState newState, params TransportBoxState[] allowedStates)
    {
        if (allowedStates.Any() && !allowedStates.Contains(State))
        {
            throw new ValidationException($"Unable to change state from {State} to {newState} ({string.Join(", ", allowedStates)} state is required for this action)");
        }
    }

    public void Error(DateTime date, string userName, string exMessage)
    {
        ChangeState(TransportBoxState.Error, date, userName, exMessage, Array.Empty<TransportBoxState>());
    }
    
    
    private static readonly Dictionary<TransportBoxState, TransportBoxStateNode> _transitions = new();

    static TransportBox()
    {
        _transitions.Add(TransportBoxState.New, new TransportBoxStateNode()
            {
                NextState = new TransportBoxAction(TransportBoxState.Opened, (box, time, userName) => box.Open(box.Code!, time, userName), condition: b => b.Code != null),
                PreviousState = new TransportBoxAction(TransportBoxState.Closed, (box, time, userName) => box.Close(time, userName)),
            }
        );
        
        _transitions.Add(TransportBoxState.Opened, new TransportBoxStateNode()
            {
                NextState = new TransportBoxAction(TransportBoxState.InTransit, (box, time, userName) => box.ToTransit(time, userName)),
                PreviousState = new TransportBoxAction(TransportBoxState.New, (box, time, userName) => box.Reset(time, userName)),
            }
        );
        
        _transitions.Add(TransportBoxState.InTransit, new TransportBoxStateNode()
            {
                NextState = new TransportBoxAction(TransportBoxState.Received, (box, time, userName) => box.Receive(time, userName)),
                PreviousState = new TransportBoxAction(TransportBoxState.Opened, (box, time, userName) => box.Open(box.Code!, time, userName), condition: b => b.Code != null),
            }
        );
        
        _transitions.Add(TransportBoxState.Received, new TransportBoxStateNode());
        
        _transitions.Add(TransportBoxState.InSwap, new TransportBoxStateNode()
            {
                NextState = new TransportBoxAction(TransportBoxState.Stocked, (box, time, userName) => box.ToPick(time, userName)),
            }
        );
        
        _transitions.Add(TransportBoxState.Stocked, new TransportBoxStateNode()
            {
                NextState = new TransportBoxAction(TransportBoxState.Closed, (box, time, userName) => box.Close(time, userName)),
                //PreviousState = new TransportBoxAction(TransportBoxState.InSwap, (box, time, userName) => box.ToSwap(time, userName))
            }
        );
        
        _transitions.Add(TransportBoxState.Closed, new TransportBoxStateNode());
        
        _transitions.Add(TransportBoxState.Reserve, new TransportBoxStateNode()
            {
                NextState = new TransportBoxAction(TransportBoxState.Received, (box, time, userName) => box.Receive(time, userName)),
                PreviousState = new TransportBoxAction(TransportBoxState.Opened, (box, time, userName) => box.Open(box.Code!, time, userName), condition: b => b.Code != null),
            }
        );
    }

}