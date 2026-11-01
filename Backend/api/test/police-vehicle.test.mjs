import test from 'node:test';
import assert from 'node:assert/strict';
import {createRoom} from '../multiplayer.mjs';
const officer=n=>({characterId:'officer-'+n,uid:String(n),name:'Oficial '+n,firstName:'Oficial',model:'bybeyby',world:'centro_teste',accountRole:'admin',adminLevel:1,organization:{code:'pm'}});
const civilian={characterId:'civilian',uid:'20',name:'Civil Teste',firstName:'Civil',model:'bybeyby',world:'centro_teste'};
const frame=(sequence,extra={})=>({clientId:'11111111-1111-4111-8111-111111111111',sequence,x:10,y:0,z:10,yaw:0,speed:0,jumping:false,jumpPhase:0,...extra});
function setup(options={}){
 const room=createRoom({capacity:4,...options}),p=officer(1);
 room.sync(p,'a',frame(1));room.duty(p,'a',{enabled:true});room.patrol(p,'carpolicia');
 return {room,p};
}
test('police driver enters the owned car, reserves a visible seat during animation and exits beside it',()=>{
 const {room,p}=setup();
 assert.equal(room.sync(civilian,'b',frame(1)).policeVehicleAvailable,true);
 assert.equal(room.vehicle(p,'a',{action:'enter',carId:'pm-1'}).driving,true);
 const during=room.sync(p,'a',frame(2));
 assert.equal(during.selfPoliceCarId,'pm-1');assert.equal(during.policeCars[0].driverUid,'1');
 assert.equal(during.policeCars[0].z,14,'walk-to-door frames do not move the vehicle');
 const observer=room.sync(civilian,'b',frame(2));
 assert.equal(observer.peers[0].policeCarId,'pm-1');assert.equal(observer.peers[0].driving,true);
 assert.equal(observer.peers[0].z,14);assert.equal(observer.selfInPoliceCar,false);
 const exit=room.vehicle(p,'a',{action:'exit',carId:'pm-1'});
 assert.equal(exit.driving,false);assert.equal(exit.x,7.9);assert.equal(exit.z,14);
 const after=room.sync(civilian,'b',frame(3));
 assert.equal(after.policeCars[0].driverUid,'');assert.equal(after.peers[0].policeCarId,'');assert.equal(after.peers[0].driving,false);
 assert.throws(()=>room.vehicle(p,'a',{action:'exit',carId:'pm-1'}),e=>e.code==='NOT_DRIVER','exit cannot be used as a free teleport');
});
test('only an authenticated on-duty PM owner can enter nearby without restraints or another vehicle',()=>{
 const {room,p}=setup();const other=officer(2);
 room.sync(other,'b',frame(1));room.duty(other,'b',{enabled:true});
 assert.throws(()=>room.vehicle(other,'b',{action:'enter',carId:'pm-1'}),e=>e.code==='POLICE_CAR_FORBIDDEN');
 assert.throws(()=>room.vehicle(p,'wrong-session',{action:'enter',carId:'pm-1'}),e=>e.code==='NOT_IN_ROOM');
 room.sync(p,'a',frame(2,{z:0}));
 assert.throws(()=>room.vehicle(p,'a',{action:'enter',carId:'pm-1'}),e=>e.code==='TARGET_TOO_FAR');
 room.sync(p,'a',frame(3,{z:11}));room.custody(other,'cuff','1');
 assert.throws(()=>room.vehicle(p,'a',{action:'enter',carId:'pm-1'}),e=>e.code==='VEHICLE_ACTIVE');
 room.custody(other,'uncuff','1');
 room.sync(p,'a',frame(4,{z:11,driving:true}));
 assert.throws(()=>room.vehicle(p,'a',{action:'enter',carId:'pm-1'}),e=>e.code==='VEHICLE_ACTIVE');
 room.sync(p,'a',frame(5,{z:11}));
 assert.throws(()=>room.vehicle({...p,organization:{code:'ems'}},'a',{action:'enter',carId:'pm-1'}),e=>e.code==='POLICE_ONLY');
 room.duty(p,'a',{enabled:false});
 assert.throws(()=>room.vehicle(p,'a',{action:'enter',carId:'pm-1'}),e=>e.code==='POLICE_ONLY');
});
test('driving frames move only the authorized car and its cuffed passenger; exit preserves passenger',()=>{
 const {room,p}=setup();room.sync(civilian,'b',frame(1,{x:11}));
 room.custody(p,'cuff','20');room.patrol(p,'put','20');
 room.vehicle(p,'a',{action:'enter',carId:'pm-1'});
 room.sync(p,'a',frame(2,{policeCarId:'pm-1',driving:true,x:17,z:19,yaw:90,speed:5}));
 const observed=room.sync(civilian,'b',frame(2));
 assert.equal(observed.policeCars[0].x,17);assert.equal(observed.policeCars[0].z,19);assert.equal(observed.policeCars[0].yaw,90);
 assert.equal(observed.selfInPoliceCar,true);assert.equal(observed.selfX,17);assert.equal(observed.selfZ,19);
 assert.equal(observed.policeCars[0].occupantUid,'20');
 assert.throws(()=>room.vehicle(p,'a',{action:'exit',carId:'pm-1'}),e=>e.code==='VEHICLE_MOVING');
 room.sync(p,'a',frame(3,{policeCarId:'pm-1',driving:true,x:17,z:19,yaw:90,speed:0}));
 const exit=room.vehicle(p,'a',{action:'exit',carId:'pm-1'});assert.equal(exit.driving,false);
 const stopped=room.sync(civilian,'b',frame(3));assert.equal(stopped.policeCars[0].occupantUid,'20');assert.equal(stopped.selfInPoliceCar,true);
});
test('put ends an active escort while retaining cuffs and the rear-seat reservation',()=>{
 const {room,p}=setup();room.sync(civilian,'b',frame(1,{x:11}));
 room.custody(p,'cuff','20');room.custody(p,'follow','20');
 assert.equal(room.sync(civilian,'b',frame(2)).selfEscortedBy,'1');
 room.patrol(p,'put','20');
 let seated=room.sync(civilian,'b',frame(3,{x:99,z:99,speed:4.5,jumping:true,jumpPhase:.5}));
 assert.equal(seated.selfEscortedBy,'');assert.equal(seated.selfCuffed,true);assert.equal(seated.selfInPoliceCar,true);
 assert.equal(seated.policeCars[0].occupantUid,'20');assert.equal(seated.policeCars[0].driverUid,'');
 assert.equal(seated.selfX,10);assert.equal(seated.selfZ,14,'the server ignores movement from a seated detainee');
 const observer=room.sync(p,'a',frame(2,{x:12,z:12,speed:1.65}));
 const peer=observer.peers.find(item=>item.uid==='20');
 assert.equal(peer.escortedBy,'');assert.equal(peer.cuffed,true);assert.equal(peer.inPoliceCar,true);
 assert.equal(peer.x,10);assert.equal(peer.z,14,'walking away does not resume the escort');
 assert.equal(peer.speed,0);assert.equal(peer.jumping,false);
 room.patrol(p,'eject','20');seated=room.sync(civilian,'b',frame(4));
 assert.equal(seated.selfInPoliceCar,false);assert.equal(seated.selfCuffed,true);assert.equal(seated.selfEscortedBy,'');
});
test('forged car poses, malformed actions and reusing a reservation from another session are rejected',()=>{
 const {room,p}=setup();room.sync(civilian,'b',frame(1));
 assert.throws(()=>room.sync(civilian,'b',frame(2,{driving:true,policeCarId:'pm-1'})),e=>e.code==='POLICE_CAR_FORBIDDEN');
 assert.throws(()=>room.sync(p,'a',frame(2,{driving:true,policeCarId:'pm-1'})),e=>e.code==='POLICE_CAR_FORBIDDEN');
 for(const body of [{action:'enter',carId:'pm-1',x:100},{action:'teleport',carId:'pm-1'},{action:'enter',carId:'x'.repeat(100)}])
  assert.throws(()=>room.vehicle(p,'a',body),e=>e.code==='INVALID_VEHICLE_ACTION');
 room.vehicle(p,'a',{action:'enter',carId:'pm-1'});
 assert.throws(()=>room.sync(p,'a',frame(3,{driving:true,policeCarId:'pm-2'})),e=>e.code==='POLICE_CAR_FORBIDDEN');
 assert.throws(()=>room.sync(p,'a',frame(3,{driving:false,policeCarId:'pm-1'})),e=>e.code==='POLICE_CAR_FORBIDDEN');
 assert.throws(()=>room.sync(p,'new-session',frame(1,{driving:true,policeCarId:'pm-1'})),e=>e.code==='POLICE_CAR_FORBIDDEN');
 assert.equal(room.sync(civilian,'b',frame(3)).policeCars[0].driverUid,'');
});
test('put and eject cannot change the detained occupant while the police driver is moving',()=>{
 const {room,p}=setup();room.sync(civilian,'b',frame(1,{x:11}));
 room.custody(p,'cuff','20');room.patrol(p,'put','20');room.vehicle(p,'a',{action:'enter',carId:'pm-1'});
 room.sync(p,'a',frame(2,{driving:true,policeCarId:'pm-1',z:14,speed:2}));
 assert.throws(()=>room.patrol(p,'eject','20'),e=>e.code==='VEHICLE_MOVING');
 assert.equal(room.sync(civilian,'b',frame(2)).policeCars[0].occupantUid,'20');
 room.sync(p,'a',frame(3,{driving:true,policeCarId:'pm-1',z:14,speed:0}));
 room.patrol(p,'eject','20');
 room.sync(p,'a',frame(4,{driving:true,policeCarId:'pm-1',z:14,speed:2}));
 assert.throws(()=>room.patrol(p,'put','20'),e=>e.code==='VEHICLE_MOVING');
 assert.equal(room.sync(civilian,'b',frame(3)).policeCars[0].occupantUid,'');
 room.sync(p,'a',frame(5,{driving:true,policeCarId:'pm-1',z:14,speed:0}));
 room.patrol(p,'put','20');
 assert.equal(room.sync(civilian,'b',frame(4)).policeCars[0].occupantUid,'20');
});
test('duty loss, session logout and expired presence remove driver/car without a ghost occupant',()=>{
 for(const cleanup of ['duty','logout','expire']){
  let now=1000;const {room,p}=setup({clock:()=>now,ttl:2000});
  room.sync(civilian,'b',frame(1));room.custody(p,'cuff','20');room.patrol(p,'put','20');room.vehicle(p,'a',{action:'enter',carId:'pm-1'});
  if(cleanup==='duty')room.duty(p,'a',{enabled:false});
  else if(cleanup==='logout')room.removeSession('a');
  else {now+=2001;}
  const after=room.sync(civilian,'b',frame(2));
  assert.equal(after.policeCars.length,0,cleanup);assert.equal(after.selfInPoliceCar,false,cleanup);assert.equal(after.selfCuffed,false,cleanup);
 }
});
