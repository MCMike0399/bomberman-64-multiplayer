extends SceneTree
# Diagnostic only: does not qualify the product or alter any production preset.
class DepthEffect extends CompositorEffect:
    var output_dir: String
    var captured := false
    var calls := 0
    var armed := false
    var multisampled := true
    func _init():
        effect_callback_type = EFFECT_CALLBACK_TYPE_POST_TRANSPARENT
        access_resolved_depth = false # Observe existing resource; do not force an extra resolve.
    func _render_callback(_kind: int, data: RenderData):
        calls += 1
        if not armed or captured:
            return
        var buffers := data.get_render_scene_buffers() as RenderSceneBuffersRD
        var rd := RenderingServer.get_rendering_device()
        var ms := buffers.get_depth_layer(0, multisampled)
        var resolved := buffers.get_depth_layer(0, false)
        if not ms.is_valid() or not resolved.is_valid():
            return
        captured = true
        var source := RDShaderSource.new()
        source.source_compute = """#version 450
layout(local_size_x=8,local_size_y=8,local_size_z=1) in;
layout(set=0,binding=0) uniform sampler2DMS raw_depth;
layout(set=0,binding=1) uniform sampler2D resolved_depth;
layout(set=0,binding=2,std430) buffer Output { vec4 values[]; } result;
void main(){ivec2 p=ivec2(gl_GlobalInvocationID.xy);ivec2 s=textureSize(resolved_depth,0);if(any(greaterThanEqual(p,s)))return;
result.values[p.y*s.x+p.x]=vec4(texelFetch(raw_depth,p,0).r,texelFetch(raw_depth,p,1).r,texelFetch(resolved_depth,p,0).r,1.0);}
"""
        if not multisampled:
            source.source_compute = source.source_compute.replace("sampler2DMS raw_depth", "sampler2D raw_depth").replace("texelFetch(raw_depth,p,1)", "texelFetch(raw_depth,p,0)")
        var spirv := rd.shader_compile_spirv_from_source(source)
        if not spirv.get_stage_compile_error(RenderingDevice.SHADER_STAGE_COMPUTE).is_empty():
            push_error(spirv.get_stage_compile_error(RenderingDevice.SHADER_STAGE_COMPUTE)); return
        var shader := rd.shader_create_from_spirv(spirv)
        var pipeline := rd.compute_pipeline_create(shader)
        var sampler_state := RDSamplerState.new()
        var sampler := rd.sampler_create(sampler_state)
        var size := buffers.get_internal_size()
        var output := rd.storage_buffer_create(size.x * size.y * 16)
        var raw_uniform := RDUniform.new(); raw_uniform.uniform_type = RenderingDevice.UNIFORM_TYPE_SAMPLER_WITH_TEXTURE; raw_uniform.binding = 0
        raw_uniform.add_id(sampler); raw_uniform.add_id(ms)
        var resolved_uniform := RDUniform.new(); resolved_uniform.uniform_type = RenderingDevice.UNIFORM_TYPE_SAMPLER_WITH_TEXTURE; resolved_uniform.binding = 1
        resolved_uniform.add_id(sampler); resolved_uniform.add_id(resolved)
        var out_uniform := RDUniform.new(); out_uniform.uniform_type = RenderingDevice.UNIFORM_TYPE_STORAGE_BUFFER; out_uniform.binding = 2; out_uniform.add_id(output)
        var uniform_set := rd.uniform_set_create([raw_uniform,resolved_uniform,out_uniform],shader,0)
        var list := rd.compute_list_begin()
        rd.compute_list_bind_compute_pipeline(list,pipeline)
        rd.compute_list_bind_uniform_set(list,uniform_set,0)
        rd.compute_list_dispatch(list,ceili(size.x/8.0),ceili(size.y/8.0),1)
        rd.compute_list_end()
        var bytes := rd.buffer_get_data(output)
        var file := FileAccess.open(output_dir + "/depth-samples.bin",FileAccess.WRITE); file.store_buffer(bytes); file.close()
        var metadata := {"diagnosticOnly":true,"width":size.x,"height":size.y,"calls":calls,"accessResolvedDepth":false,
            "rawFormat":rd.texture_get_format(ms).format,"rawSamples":rd.texture_get_format(ms).samples,
            "resolvedFormat":rd.texture_get_format(resolved).format,"resolvedSamples":rd.texture_get_format(resolved).samples,
            "layout":"little-endian float32 sample0,sample1,resolved,one per pixel; row-major",
            "planes":[{"pixel":[115,128],"worldDepth":5.0},{"pixel":[192,128],"worldDepth":10.0},{"pixel":[269,128],"worldDepth":20.0}],
            "camera":{"projection":"orthographic","near":0.1,"far":100.0,"size":10.0},
            "ssao":true,"ssil":true,"dof":"Circle/Low/jitterOn/amount0.075/near7.5transition2/far12transition10",
            "stage":"post-transparent; built-in postprocessing follows; no explicit resolve request",
            "engine":Engine.get_version_info(),"bytes":bytes.size(),"driver":RenderingServer.get_current_rendering_driver_name(),"device":RenderingServer.get_video_adapter_name()}
        file=FileAccess.open(output_dir + "/depth-resources.json",FileAccess.WRITE); file.store_string(JSON.stringify(metadata,"  ")); file.close()
        for rid in [uniform_set,output,sampler,pipeline,shader]: rd.free_rid(rid)

var effect: DepthEffect
var output_dir: String
func _initialize():
    output_dir=OS.get_cmdline_user_args()[0]
    DirAccess.make_dir_recursive_absolute(output_dir)
    call_deferred("run")
func run():
    root.size=Vector2i(384,256);root.msaa_3d=Viewport.MSAA_DISABLED if OS.get_cmdline_user_args().has("--no-msaa") else Viewport.MSAA_2X
    var scene:=Node3D.new();root.add_child(scene)
    var environment:=WorldEnvironment.new();environment.environment=Environment.new()
    environment.environment.background_mode=Environment.BG_COLOR;environment.environment.background_color=Color.BLACK
    environment.environment.ssao_enabled=true;environment.environment.ssil_enabled=true
    effect=DepthEffect.new();effect.output_dir=output_dir;effect.multisampled=root.msaa_3d==Viewport.MSAA_2X
    environment.compositor=Compositor.new();environment.compositor.compositor_effects=[effect];scene.add_child(environment)
    var camera:=Camera3D.new();camera.projection=Camera3D.PROJECTION_ORTHOGONAL;camera.size=10;camera.near=.1;camera.far=100
    var attributes:=CameraAttributesPractical.new();attributes.dof_blur_amount=.075
    attributes.dof_blur_near_enabled=true;attributes.dof_blur_near_distance=7.5;attributes.dof_blur_near_transition=2
    attributes.dof_blur_far_enabled=true;attributes.dof_blur_far_distance=12;attributes.dof_blur_far_transition=10
    camera.attributes=attributes;scene.add_child(camera);camera.current=true
    RenderingServer.camera_attributes_set_dof_blur_quality(RenderingServer.DOF_BLUR_QUALITY_LOW,true)
    RenderingServer.camera_attributes_set_dof_blur_bokeh_shape(RenderingServer.DOF_BOKEH_CIRCLE)
    var shader:=Shader.new();shader.code="shader_type spatial; render_mode unshaded; void fragment(){ ALBEDO=vec3(mod(floor(UV.x*32.0)+floor(UV.y*32.0),2.0)); }"
    for i in 3:
        var mesh:=MeshInstance3D.new();mesh.mesh=QuadMesh.new();mesh.mesh.size=Vector2(2.5,5)
        mesh.material_override=ShaderMaterial.new();mesh.material_override.shader=shader;scene.add_child(mesh)
        mesh.position=Vector3((i-1)*3,0,-[5,10,20][i])
    create_timer(60).timeout.connect(func(): quit(3))
    for i in 24: await process_frame
    effect.armed=true
    var deadline:=Time.get_ticks_msec()+20000
    while not FileAccess.file_exists(output_dir+"/depth-resources.json") and Time.get_ticks_msec()<deadline:await process_frame
    await RenderingServer.frame_post_draw
    root.get_texture().get_image().save_png(output_dir+"/scene.png")
    print("DEPTH_PROBE ", FileAccess.file_exists(output_dir+"/depth-resources.json"))
    root.remove_child(scene);scene.free();effect=null
    quit(0 if FileAccess.file_exists(output_dir+"/depth-resources.json") else 1)
