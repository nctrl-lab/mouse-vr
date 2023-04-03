import socket
import time

HOST = "10.8.1.136"
PORT = '22223'

s = socket.create_connection((HOST, PORT), timeout=1)
s.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
print(f'Connected to {HOST}:{PORT}')

# while True:
    # data = s.recv(1024)
    # print(data.decode('utf8'), end='')
    # time.sleep(0.01)

# for i in range(10):
    # msg = f"Console.teleport(0, {i}, 0)\n"
    # s.sendall(msg.encode('utf8'))
    # time.sleep(0.5)
    # print(f'Sending: {msg}')

msg = f"Console.toggle_blanking()\n"
print(f'Sending: {msg}')
s.send(msg.encode('utf8'))

time.sleep(2)

msg = f"Console.toggle_blanking()\n"
print(f'Sending: {msg}')
s.send(msg.encode('utf8'))

time.sleep(2)

for i in range(40):
    msg = f"Console.teleport({i/30:0.2f}, {i/10:0.2f}, 0.2)\n"
    s.send(msg.encode('utf8'))
    print(f'Sending: {msg}')
    time.sleep(0.01)

time.sleep(1)

msg = f"Console.teleport(0, 0, 0.2)\n"
s.send(msg.encode('utf8'))
print(f'Sending: {msg}')

time.sleep(1)

for i in range(40):
    msg = f"Model.move('door', {(i-20)/20:0.2f}, 2, 0.5)\n"
    s.sendall(msg.encode('utf8'))
    print(f'Sending: {msg}')
    time.sleep(0.01)
    
time.sleep(1)
msg = f"Model.move('door', 0, 2, 0.5)\n"

time.sleep(1)

# msg = f"Quit\n"
# s.send(msg.encode('utf8'))
# print(f'Sending: {msg}')

s.close()
