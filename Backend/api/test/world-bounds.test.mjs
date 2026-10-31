import test from 'node:test';
import assert from 'node:assert/strict';
import {WORLD_BOUNDS,getWorldBounds,isPositionWithinWorld} from '../world-bounds.mjs';

test('official city contract is immutable and keeps the legacy world',()=>{
 assert.deepEqual(getWorldBounds('centro_teste'),{minX:-4200,maxX:4200,minY:-200,maxY:1000,minZ:-4200,maxZ:4200});
 assert.ok(Object.isFrozen(WORLD_BOUNDS));assert.ok(Object.isFrozen(WORLD_BOUNDS.centro_teste));
 assert.throws(()=>{WORLD_BOUNDS.centro_teste.maxX=5000;},TypeError);
 assert.throws(()=>{WORLD_BOUNDS.other_world={minX:-9999};},TypeError);
});

test('legacy spawn, extended interior and every boundary are accepted; outside and nonfinite coordinates are denied',()=>{
 const within=position=>isPositionWithinWorld('centro_teste',position);
 for(const position of [{x:0,y:-.1,z:0},{x:0,y:.08,z:0},{x:-59,y:-1,z:59},{x:45,y:6,z:-45},{x:700,y:30,z:-800},{x:-4200,y:-200,z:-4200},{x:4200,y:1000,z:4200},
  {x:-4200,y:0,z:0},{x:4200,y:0,z:0},{x:0,y:-200,z:0},{x:0,y:1000,z:0},{x:0,y:0,z:-4200},{x:0,y:0,z:4200}])assert.equal(within(position),true,JSON.stringify(position));
 for(const position of [{x:-4200.001,y:0,z:0},{x:4200.001,y:0,z:0},{x:0,y:-200.001,z:0},{x:0,y:1000.001,z:0},
  {x:0,y:0,z:-4200.001},{x:0,y:0,z:4200.001},{x:NaN,y:0,z:0},{x:0,y:Infinity,z:0},{x:0,y:0,z:-Infinity},
  {x:'0',y:0,z:0},{x:0,z:0},null,undefined])assert.equal(within(position),false);
 for(const world of ['other_world','__proto__','constructor','',null,undefined]){
  assert.equal(getWorldBounds(world),null);assert.equal(isPositionWithinWorld(world,{x:0,y:0,z:0}),false);
 }
});
