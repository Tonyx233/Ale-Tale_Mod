"""Read-only native prefab audit; requires existing UnityPy tools."""
import sys
from pathlib import Path
sys.path.insert(0, str(Path.cwd()/'horse-riding-work/python'))
import UnityPy
for asset in (Path.cwd()/'Ale and Tale Tavern_Data').glob('*.assets'):
    env=UnityPy.load(str(asset))
    for obj in env.objects:
        if obj.type.name!='GameObject': continue
        go=obj.read()
        if 'spider' not in go.m_Name.lower(): continue
        print(asset.name, obj.path_id, go.m_Name, 'layer',go.m_Layer)
        for slot in go.m_Component:
            comp=slot.component
            if comp.type.name=='MonoBehaviour':
                value=comp.deref().parse_as_object(check_read=False)
                print(' ',comp.type.name,value.m_Script.read().m_ClassName)
            else: print(' ',comp.type.name)
