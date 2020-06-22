# quick start to jupyter server

1. install anaconda (should be latest version)
2. run jupyter notebook --generate-config
3. edit C:\Users\USERNAME\.jupyter\jupyter_notebook_config.py (for windows)
4. change the following:
* add password: c.NotebookApp.password = 'password'
* change ip host to receive publicly connections: c.NotebookApp.ip = '*'
* use a non conventional port: c.NotebookApp.port = 9999
5. run jupyter and leave it open when you log off ie. use screen if linux

### to "expose" the server to the network you need to do the following
* IP - identify ip of host server (public ip if in cloud or if accessing from outside the network)
* FIREWALL - you need to allow outgoing and incoming ip / port connections from the host OS
* PORT FORWARDING - if behind router need to allow ip/port connections to route to host server
* PROXY - if behind proxy, need to provide proxy info to jupyter config

### other notes:
* better to use encrypted password ie. SHA-1 - prevents password leaks from server side
* better to use SSL / HTTPS settings - encrypts data transfer
