using System;
using XReality;
public static class BrainChecks {
    static void Expect(bool condition,string label){if(!condition)throw new Exception(label);}
    public static int Main(){
        var b=new PetBrain();Expect(b.State==Mood.Watch,"first visit");
        Expect(b.Stroke(1),"awake accepts stroke");Expect(b.State==Mood.Nuzzle&&!b.Stroke(1.5),"no restart");
        b.Tick(2.1,false);Expect(b.State==Mood.Watch,"nuzzle finishes");b.Tick(14.2,false);Expect(b.State==Mood.Sleep,"falls asleep");
        Expect(b.Stroke(15)&&b.State==Mood.EarFlick&&!b.Stroke(15.1),"sleepy ear only");b.Tick(15.7,false);Expect(b.State==Mood.Sleep,"still sleeping");
        b.OfferToy(16);b.OfferToy(16.4);b.Tick(16.5,false);Expect(b.State==Mood.Notice,"look first");
        b.Tick(16.6,false);Expect(b.State==Mood.Chase,"drag does not restart notice");b.OfferToy(16.7);Expect(!b.Stroke(16.8),"chase uninterrupted");
        b.Tick(18,true);Expect(b.State==Mood.Watch,"arrival");b.Tick(31,false);b.Tick(40,false);Expect(b.State==Mood.Watch,"wakes up naturally");
        var memory=new PlayMemory(-1,0);
        for(int i=0;i<3;i++)memory.Record(2);
        Expect(memory.Favorite==PlayMemory.Zone.Window,"learn repeated play");
        var reload=new PlayMemory(memory.Rug,memory.Window);
        Expect(reload.Favorite==PlayMemory.Zone.Window,"favorite survives reload");
        for(int i=0;i<20;i++)reload.Record(0);
        Expect(reload.Favorite==PlayMemory.Zone.Rug && reload.Rug==PlayMemory.MaxScore,"can relearn, capped scores");
        Console.WriteLine("PASS: pet transitions, cooldowns, notice before chase, arrival, rest cycle.");return 0;
    }
}
