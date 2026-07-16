import net from 'net'

/**
 * Source RCON 协议客户端
 * 连接 TShock 插件的 RconServer 获取实时日志推送
 */
export class RconClient {
  constructor(host, port, password) {
    this.host = host || '127.0.0.1'
    this.port = port || 7880
    this.password = password || ''
    this.socket = null
    this.connected = false
    this.authenticated = false

    this.onLog = null       // callback(line: string)
    this.onError = null     // callback(err: Error)
    this.onEnd = null       // callback()

    this._buffer = Buffer.alloc(0)
    this._pendingRequests = new Map()
    this._requestId = 0
    this._closed = false
  }

  /**
   * 连接并认证
   * @returns {Promise<void>}
   */
  async connect() {
    return new Promise((resolve, reject) => {
      this.socket = new net.Socket()
      this._closed = false

      this.socket.connect(this.port, this.host, () => {
        this.connected = true
        // 发送认证包
        this._sendRaw(3, this.password, 1)
          .then(resp => {
            if (resp.requestId === 1) {
              this.authenticated = true
              resolve()
            } else {
              reject(new Error('RCON 认证失败'))
            }
          })
          .catch(reject)
      })

      this.socket.on('data', data => this._onData(data))

      this.socket.on('close', () => {
        this.connected = false
        this.authenticated = false
        if (!this._closed) {
          this._closed = true
          this.onEnd?.()
        }
      })

      this.socket.on('error', err => {
        this.onError?.(err)
        if (!this._closed) {
          reject(err)
        }
      })

      setTimeout(() => {
        if (!this.authenticated) {
          reject(new Error('RCON 连接超时'))
          this.close()
        }
      }, 5000)
    })
  }

  /**
   * 发送命令
   */
  async execCommand(cmd) {
    if (!this.authenticated) throw new Error('RCON 未认证')
    const resp = await this._sendRaw(2, cmd)
    return resp.payload
  }

  /**
   * 关闭连接
   */
  close() {
    this._closed = true
    this.socket?.destroy()
    this.socket = null
    this.connected = false
    this.authenticated = false
  }

  // ═════════════════════════════════════
  //  RCON 协议实现
  // ═════════════════════════════════════

  /**
   * 发送 RCON 数据包并等待响应
   */
  _sendRaw(type, payload, requestId) {
    if (requestId === undefined) {
      requestId = ++this._requestId
    }

    const payloadBytes = Buffer.from(payload, 'utf8')
    const length = 8 + payloadBytes.length + 2    // RequestID(4) + Type(4) + payload + padding(2)
    const packet = Buffer.alloc(4 + length)        // 4 for Length field

    packet.writeInt32LE(length, 0)                  // Length
    packet.writeInt32LE(requestId, 4)               // RequestID
    packet.writeInt32LE(type, 8)                    // Type
    payloadBytes.copy(packet, 12)                   // Payload
    // Padding 2 bytes at end — already zero

    this.socket.write(packet)

    return new Promise(resolve => {
      this._pendingRequests.set(requestId, resolve)
    })
  }

  /**
   * 处理收到的数据
   */
  _onData(data) {
    this._buffer = Buffer.concat([this._buffer, data])

    while (this._buffer.length >= 4) {
      const length = this._buffer.readInt32LE(0)
      const totalPacketSize = 4 + length
      if (this._buffer.length < totalPacketSize) break

      const packet = this._buffer.slice(4, totalPacketSize)
      this._buffer = this._buffer.slice(totalPacketSize)

      const requestId = packet.readInt32LE(0)
      const type = packet.readInt32LE(4)
      // Payload: offset 8, length = total - 8 - 2(padding)
      const payloadLen = length - 10
      const payload = payloadLen > 0
        ? packet.toString('utf8', 8, 8 + payloadLen).replace(/\0+$/, '')
        : ''

      if (requestId === 0 && type === 0) {
        // 服务端主动推送的日志行
        this.onLog?.(payload)
      } else if (this._pendingRequests.has(requestId)) {
        const resolve = this._pendingRequests.get(requestId)
        this._pendingRequests.delete(requestId)
        resolve({ requestId, type, payload })
      }
    }
  }
}
