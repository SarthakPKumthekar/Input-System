#if UNITY_EDITOR || DEVELOPMENT_BUILD
using ISX.LowLevel;
using NUnit.Framework;

public class UnitTests_InputEventQueue
{
    [Test]
    [Category("Events")]
    public void TODO_CanQueueAndDequeueEvent()
    {
        var queue = new InputEventQueue(InputEvent.kBaseEventSize, 10);
        //queue.WriteEvent(new InputEvent());
        Assert.Fail();
    }
}
#endif // UNITY_EDITOR || DEVELOPMENT_BUILD
