// Authoritative playable coordinates, in Unity metres. Edges are inclusive.
// Keep the existing world code and saved coordinates when expanding the official city.
export const WORLD_BOUNDS=Object.freeze({
 centro_teste:Object.freeze({minX:-1000,maxX:1000,minY:-80,maxY:100,minZ:-1000,maxZ:1000})
});

export function getWorldBounds(world){
 return typeof world==='string'&&Object.hasOwn(WORLD_BOUNDS,world)?WORLD_BOUNDS[world]:null;
}

export function isPositionWithinWorld(world,position){
 const bounds=getWorldBounds(world);
 return bounds!==null&&position!==null&&typeof position==='object'&&
  Number.isFinite(position.x)&&Number.isFinite(position.y)&&Number.isFinite(position.z)&&
  position.x>=bounds.minX&&position.x<=bounds.maxX&&
  position.y>=bounds.minY&&position.y<=bounds.maxY&&
  position.z>=bounds.minZ&&position.z<=bounds.maxZ;
}
