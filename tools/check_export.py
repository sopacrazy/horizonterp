import bpy
bpy.ops.wm.open_mainfile(filepath=r'C:\SistemaAdr\gta\personagem_unity\ByBeyby.blend')
for o in bpy.data.objects: print(o.name,o.type,'selected',o.select_get(),'hide',o.hide_get(),o.hide_viewport,o.hide_render,'visible',o.visible_get(),'collections',[(c.name,c.hide_viewport) for c in o.users_collection])
print('view layer', [o.name for o in bpy.context.view_layer.objects])
