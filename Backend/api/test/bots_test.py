import importlib.util, pathlib, random, unittest, math
spec=importlib.util.spec_from_file_location('bots',pathlib.Path(__file__).resolve().parents[1]/'test_bots.py')
bots=importlib.util.module_from_spec(spec);spec.loader.exec_module(bots)
class BotMotionTests(unittest.TestCase):
 def test_independent_walk_only_without_jumping_or_running(self):
  motions=[bots.Motion(i,random.Random(i)) for i in range(5)]
  self.assertEqual(len({m.target for m in motions}),5)
  self.assertEqual(len({m.next_pause for m in motions}),5)
  seen=[set() for _ in motions];different_states=0;pause_times=[[] for _ in motions]
  previous=[(m.x,m.z) for m in motions];was_paused=[False]*5
  for tick in range(6000):
   moving=[]
   for i,motion in enumerate(motions):
    frame=motion.step(.1);moving.append(frame['speed']>.01);seen[i].add(frame['speed']>.01)
    paused=motion.elapsed<motion.pause_until
    if paused and not was_paused[i]:pause_times[i].append(tick)
    was_paused[i]=paused
    self.assertLessEqual(abs(frame['x']),14);self.assertLessEqual(abs(frame['z']),2.2)
    self.assertEqual(frame['y'],-.1);self.assertFalse(frame['jumping']);self.assertEqual(frame['jumpPhase'],0)
    self.assertTrue(0<=frame['yaw']<360);self.assertTrue(0<=frame['speed']<=1.650001)
    self.assertLessEqual(math.hypot(frame['x']-previous[i][0],frame['z']-previous[i][1]),.165001)
    previous[i]=(frame['x'],frame['z'])
   different_states+=len(set(moving))>1
  self.assertGreater(different_states,1000)
  for modes in seen:self.assertEqual(modes,{False,True})
  self.assertEqual(len({tuple(times) for times in pause_times}),5)
if __name__=='__main__':unittest.main()
