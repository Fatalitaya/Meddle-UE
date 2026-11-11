"""Execute a script in a named local UE editor using Epic's Python remote API."""
import json
from pathlib import Path
import sys
import time

engine, project, script = sys.argv[1:]
sys.path.insert(0, str(Path(engine) / 'Engine/Plugins/Experimental/PythonScriptPlugin/Content/Python'))
import remote_execution

remote = remote_execution.RemoteExecution()
remote.start()
try:
    for _ in range(20):
        nodes = [n for n in remote.remote_nodes if n.get('project_name') == project]
        if nodes:
            break
        time.sleep(.25)
    if len(nodes) != 1:
        raise RuntimeError(f'Expected one editor for {project}: {remote.remote_nodes}')
    remote.open_command_connection(nodes[0]['node_id'])
    result = remote.run_command(Path(script).read_text(encoding='utf-8-sig'))
    print(json.dumps(result, ensure_ascii=True))
    if not result['success']:
        sys.exit(1)
finally:
    remote.stop()
